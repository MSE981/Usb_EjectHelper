using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace UsbEjectHelper.Core;

/// <summary>
/// 可移动存储设备监视器 —— 枚举可弹出设备、通过 WMI 识别 USB 移动硬盘、
/// 监听 WM_DEVICECHANGE 变化事件。
/// </summary>
public class DeviceWatcher : IDisposable
{
    private readonly ILogger<DeviceWatcher> _logger;
    private readonly IWmiQueryService _wmiService;
    private readonly List<DeviceInfo> _devices = new();
    private readonly object _lock = new();
    private readonly System.Timers.Timer _debounceTimer;
    private volatile bool _refreshPending;

    /// <summary>
    /// 设备列表变化时触发。
    /// </summary>
    public event EventHandler<List<DeviceInfo>>? DevicesChanged;

    /// <summary>
    /// 当前已知的可弹出设备列表（线程安全副本）。
    /// </summary>
    public IReadOnlyList<DeviceInfo> Devices
    {
        get { lock (_lock) return _devices.ToList(); }
    }

    public DeviceWatcher(IWmiQueryService? wmiService = null, ILogger<DeviceWatcher>? logger = null)
    {
        _logger = logger ?? LoggerFactory.Create(b => b.AddConsole()).CreateLogger<DeviceWatcher>();
        _wmiService = wmiService ?? new WmiQueryService();

        _debounceTimer = new System.Timers.Timer(800); // 800ms 防抖
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += (_, _) =>
        {
            _refreshPending = false;
            RefreshDevices();
        };
    }

    /// <summary>
    /// 执行初始枚举并开始监听。
    /// </summary>
    public void Start()
    {
        _logger.LogInformation("DeviceWatcher 启动，开始初始枚举。");
        RefreshDevices();
    }

    /// <summary>
    /// 处理 WM_DEVICECHANGE 消息（由 MainWindow 的 WndProc 调用）。
    /// </summary>
    /// <param name="msg">消息 m.Message</param>
    /// <param name="wParam">m.WParam</param>
    /// <param name="lParam">m.LParam</param>
    public void HandleDeviceChangeMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != 0x0219) // WM_DEVICECHANGE
            return;

        int eventType = wParam.ToInt32();

        _logger.LogDebug("WM_DEVICECHANGE: eventType=0x{EventType:X4}", eventType);

        if (lParam != IntPtr.Zero)
        {
            // 尝试读取 DEV_BROADCAST_HDR
            var hdr = Marshal.PtrToStructure<DEV_BROADCAST_HDR>(lParam);
            if (hdr.dbch_devicetype == DeviceChangeParser.DBT_DEVTYP_VOLUME)
            {
                var vol = Marshal.PtrToStructure<DEV_BROADCAST_VOLUME>(lParam);
                var drives = DeviceChangeParser.ParseDriveLettersFromMask(vol.dbcv_unitmask);
                _logger.LogInformation(
                    "设备变化: event=0x{EventType:X4}, drives=[{Drives}]",
                    eventType, string.Join(", ", drives));
            }
        }

        if (DeviceChangeParser.IsRelevantEvent(eventType))
        {
            DebounceRefresh();
        }
    }

    /// <summary>
    /// 手动触发设备刷新。
    /// </summary>
    public void RefreshDevices()
    {
        try
        {
            var before = Devices.Count;
            var newDevices = EnumerateDevices();

            lock (_lock)
            {
                _devices.Clear();
                _devices.AddRange(newDevices);
            }

            var after = _devices.Count;
            _logger.LogInformation("设备刷新完成: {Before} → {After} 个可弹出设备", before, after);

            DevicesChanged?.Invoke(this, newDevices);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设备刷新失败。");
        }
    }

    /// <summary>
    /// 核心枚举逻辑 —— 结合 DriveInfo 与 WMI USB 关联。
    /// </summary>
    private List<DeviceInfo> EnumerateDevices()
    {
        var devices = new List<DeviceInfo>();

        // 1. 获取 USB 物理磁盘的 DeviceID 集合
        var usbDiskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var diskDrives = _wmiService.GetDiskDrives();
        var diskPartitionMappings = _wmiService.GetDiskPartitionMappings();
        var logicalDiskPartitionMappings = _wmiService.GetLogicalDiskPartitionMappings();
        var logicalDisks = _wmiService.GetLogicalDisks();

        // 构建磁盘信息查找表
        var diskInfoMap = new Dictionary<string, WmiDiskDrive>(StringComparer.OrdinalIgnoreCase);
        foreach (var disk in diskDrives)
        {
            diskInfoMap[disk.DeviceID] = disk;

            // 判断是否为 USB 设备
            bool isUsb = disk.InterfaceType?.Equals("USB", StringComparison.OrdinalIgnoreCase) == true ||
                         disk.PNPDeviceID?.Contains("USB", StringComparison.OrdinalIgnoreCase) == true;

            if (isUsb)
            {
                usbDiskIds.Add(disk.DeviceID);
                _logger.LogDebug("检测到 USB 磁盘: {DeviceID} ({Model})", disk.DeviceID, disk.Model);
            }
        }

        // 2. 构建 磁盘 → 分区 映射
        //    从 Win32_DiskDriveToDiskPartition: Antecedent=磁盘路径, Dependent=分区路径
        var diskToPartitions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in diskPartitionMappings)
        {
            var diskId = ExtractDeviceId(mapping.Antecedent);
            var partitionId = ExtractDeviceId(mapping.Dependent);
            if (!string.IsNullOrEmpty(diskId) && !string.IsNullOrEmpty(partitionId))
            {
                if (!diskToPartitions.ContainsKey(diskId))
                    diskToPartitions[diskId] = new List<string>();
                diskToPartitions[diskId].Add(partitionId);
            }
        }

        // 3. 构建 分区 → 逻辑盘盘符 映射
        //    从 Win32_LogicalDiskToPartition: Antecedent=逻辑盘路径, Dependent=分区路径
        var partitionToDrive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in logicalDiskPartitionMappings)
        {
            var driveLetter = ExtractDeviceId(mapping.Antecedent);
            var partitionId = ExtractDeviceId(mapping.Dependent);
            if (!string.IsNullOrEmpty(driveLetter) && !string.IsNullOrEmpty(partitionId))
            {
                partitionToDrive[partitionId] = driveLetter;
            }
        }

        // 4. 构建盘符 → WMI 逻辑盘信息 映射
        var wmiDriveMap = new Dictionary<string, WmiLogicalDisk>(StringComparer.OrdinalIgnoreCase);
        foreach (var disk in logicalDisks)
        {
            // DeviceID 通常是 "C:" 格式
            var drive = disk.DeviceID.TrimEnd('\\').Trim();
            if (drive.Length >= 2 && drive[1] == ':')
            {
                wmiDriveMap[drive] = disk;
            }
        }

        // 5. 从 DriveInfo 获取系统角度信息
        var allDrives = DriveInfo.GetDrives();

        // 判断系统盘
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? string.Empty;
        systemDrive = systemDrive.TrimEnd('\\').Trim();

        foreach (var drive in allDrives)
        {
            if (!drive.IsReady) continue;

            var driveLetter = drive.Name.TrimEnd('\\').Trim();

            // 排除系统盘
            if (string.Equals(driveLetter, systemDrive, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("排除系统盘: {Drive}", driveLetter);
                continue;
            }

            bool isRemovable = drive.DriveType == DriveType.Removable;
            bool isFixed = drive.DriveType == DriveType.Fixed;
            bool isUsb = false;
            string diskModel = string.Empty;
            string interfaceType = string.Empty;

            // 对于 Fixed 类型，通过 WMI 判断是否为 USB 设备
            if (isFixed)
            {
                // 查找盘符对应的分区，再找分区对应的磁盘
                // 4a. 盘符 → 分区
                if (partitionToDrive.TryGetValue(driveLetter, out var partitionId))
                {
                    // 4b. 分区 → 磁盘
                    foreach (var (diskId, partitions) in diskToPartitions)
                    {
                        if (partitions.Contains(partitionId, StringComparer.OrdinalIgnoreCase))
                        {
                            isUsb = usbDiskIds.Contains(diskId);
                            if (diskInfoMap.TryGetValue(diskId, out var diskInfo))
                            {
                                diskModel = diskInfo.Model;
                                interfaceType = diskInfo.InterfaceType;
                            }
                            break;
                        }
                    }
                }

                // 非 USB 的固定盘排除
                if (!isUsb)
                {
                    _logger.LogDebug("排除非 USB 固定盘: {Drive}", driveLetter);
                    continue;
                }
            }

            // 获取 WMI 补充信息
            wmiDriveMap.TryGetValue(driveLetter, out var wmiDisk);

            var deviceInfo = new DeviceInfo
            {
                DriveLetter = driveLetter,
                VolumeLabel = drive.VolumeLabel,
                TotalSize = drive.TotalSize,
                AvailableFreeSpace = drive.AvailableFreeSpace,
                FileSystem = drive.DriveFormat,
                DriveType = drive.DriveType,
                VolumeGuid = GetVolumeGuid(driveLetter),
                DevicePath = GetDevicePath(driveLetter),
                IsUsb = isUsb || isRemovable,
                DiskModel = diskModel,
                InterfaceType = interfaceType
            };

            devices.Add(deviceInfo);
            _logger.LogInformation(
                "可弹出设备: {Drive} [{Label}] ({Fs}) {Type} USB={IsUsb} {Model}",
                deviceInfo.DriveLetter, deviceInfo.VolumeLabel, deviceInfo.FileSystem,
                deviceInfo.DriveType, deviceInfo.IsUsb, deviceInfo.DiskModel);
        }

        return devices;
    }

    /// <summary>
    /// 提取 WMI 路径中的 DeviceID 部分。
    /// 例如 "Win32_DiskDrive.DeviceID=\"\\\\.\\PHYSICALDRIVE1\"" → "\\\\.\\PHYSICALDRIVE1"
    /// </summary>
    private static string? ExtractDeviceId(string wmiPath)
    {
        if (string.IsNullOrEmpty(wmiPath)) return null;
        var idx = wmiPath.IndexOf("=\"", StringComparison.Ordinal);
        if (idx < 0) return null;
        var start = idx + 2;
        var end = wmiPath.IndexOf('"', start);
        if (end < 0) return null;
        return wmiPath.Substring(start, end - start);
    }

    /// <summary>
    /// 获取卷 GUID 路径。
    /// </summary>
    private static string GetVolumeGuid(string driveLetter)
    {
        try
        {
            var sb = new System.Text.StringBuilder(128);
            if (NativeMethods.GetVolumeNameForVolumeMountPoint(driveLetter + "\\", sb, (uint)sb.Capacity))
            {
                return sb.ToString().TrimEnd('\\');
            }
        }
        catch { }
        return string.Empty;
    }

    /// <summary>
    /// 获取 NT 设备路径（通过 QueryDosDevice）。
    /// </summary>
    private static string GetDevicePath(string driveLetter)
    {
        try
        {
            var sb = new System.Text.StringBuilder(256);
            if (NativeMethods.QueryDosDevice(driveLetter, sb, (uint)sb.Capacity) > 0)
            {
                return sb.ToString();
            }
        }
        catch { }
        return string.Empty;
    }

    /// <summary>
    /// 防抖刷新 —— 在短时间内合并多次事件。
    /// </summary>
    private void DebounceRefresh()
    {
        if (_refreshPending) return;
        _refreshPending = true;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
        if (_wmiService is IDisposable disposable)
            disposable.Dispose();
        GC.SuppressFinalize(this);
    }

    // Win32 结构体定义（仅用于解析 WM_DEVICECHANGE 消息）
    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_HDR
    {
        public uint dbch_size;
        public uint dbch_devicetype;
        public uint dbch_reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEV_BROADCAST_VOLUME
    {
        public uint dbcv_size;
        public uint dbcv_devicetype;
        public uint dbcv_reserved;
        public uint dbcv_unitmask;
        public ushort dbcv_flags;
    }
}
