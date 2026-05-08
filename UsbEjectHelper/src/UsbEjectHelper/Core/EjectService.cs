using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.InteropServices;
using System.Text;

namespace UsbEjectHelper.Core;

/// <summary>
/// 弹出结果状态枚举。
/// </summary>
public enum EjectResult
{
    /// <summary>弹出成功</summary>
    Success,
    /// <summary>设备忙，有占用（来源不明确）</summary>
    DeviceBusy,
    /// <summary>设备被 PNP veto 拒绝弹出，附带 veto 类型 / 进程名</summary>
    DeviceBusyVetoed,
    /// <summary>权限不足</summary>
    AccessDenied,
    /// <summary>设备不存在</summary>
    DeviceNotFound,
    /// <summary>API 调用失败</summary>
    ApiFailure,
    /// <summary>未知错误</summary>
    UnknownError
}

/// <summary>
/// 安全弹出服务 —— 调用 Windows API 尝试弹出可移动设备，失败时返回结构化原因。
/// </summary>
public class EjectService : IEjectService, IDisposable
{
    private readonly ILogger<EjectService> _logger;

    public EjectService(ILogger<EjectService>? logger = null)
    {
        _logger = logger ?? NullLogger<EjectService>.Instance;
    }

    /// <summary>
    /// 尝试弹出指定盘符的设备。
    /// </summary>
    /// <param name="driveLetter">盘符，如 "E:"</param>
    /// <returns>弹出结果和可读消息</returns>
    public (EjectResult Result, string Message) TryEject(string driveLetter)
    {
        var normalized = VolumeResolver.NormalizeDriveLetter(driveLetter);
        if (string.IsNullOrEmpty(normalized))
        {
            return (EjectResult.DeviceNotFound, $"无效的盘符：{driveLetter}");
        }

        _logger.LogInformation("尝试弹出 {Drive}", normalized);

        try
        {
            // 方法 1: Windows Shell "安全删除硬件" 路径（保守，优先）
            var shellResult = EjectViaShell(normalized);
            if (shellResult == EjectResult.Success)
            {
                _logger.LogInformation("{Drive} 弹出成功（Shell 路径）。", normalized);
                return (EjectResult.Success, $"设备 {normalized} 已安全弹出，可以拔出。");
            }

            // 方法 2: CM_Request_Device_Eject（SetupDi 完整链路）
            var (cmResult, cmDetail) = EjectViaCfgMgr32(normalized);
            if (cmResult == EjectResult.Success)
            {
                _logger.LogInformation("{Drive} 通过 CM API 弹出成功。", normalized);
                return (EjectResult.Success, $"设备 {normalized} 已安全弹出，可以拔出。");
            }

            _logger.LogWarning("{Drive} 弹出失败: Shell={Shell}, CM={Cm}, Detail={Detail}",
                normalized, shellResult, cmResult, cmDetail);

            if (shellResult == EjectResult.AccessDenied || cmResult == EjectResult.AccessDenied)
            {
                return (EjectResult.AccessDenied,
                    $"权限不足，无法弹出 {normalized}。请尝试以管理员身份运行。");
            }

            if (cmResult == EjectResult.DeviceBusyVetoed)
            {
                return (EjectResult.DeviceBusyVetoed,
                    $"设备 {normalized} 被系统拒绝弹出：{cmDetail}\n建议执行「扫描占用」查看详情。");
            }

            return (EjectResult.DeviceBusy,
                $"设备 {normalized} 正忙，无法弹出。可能是文件被占用或仍有程序在使用该设备。\n建议执行「扫描占用」查看详情。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "弹出 {Drive} 时发生异常", normalized);
            return (EjectResult.ApiFailure, $"弹出失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 通过 Windows Shell COM 尝试弹出（保守路径，等效于右键"弹出"）。
    /// 不做 lock/dismount/eject media 的强制卸载。
    /// </summary>
    private EjectResult EjectViaShell(string driveLetter)
    {
        object? shell = null;
        object? drivesFolder = null;
        object? driveItem = null;
        object? verbs = null;
        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null)
            {
                _logger.LogWarning("Shell.Application COM 类型不可用。");
                return EjectResult.ApiFailure;
            }

            shell = Activator.CreateInstance(shellType)!;
            // Namespace(17) = ssfDRIVES
            drivesFolder = shellType.InvokeMember("Namespace", System.Reflection.BindingFlags.InvokeMethod,
                null, shell, new object[] { 17 })!;
            driveItem = drivesFolder.GetType().InvokeMember("ParseName", System.Reflection.BindingFlags.InvokeMethod,
                null, drivesFolder, new object[] { driveLetter });

            if (driveItem == null)
            {
                _logger.LogDebug("Shell 找不到驱动器的文件夹项: {Drive}", driveLetter);
                return EjectResult.DeviceNotFound;
            }

            // 遍历可用动词，查找"弹出"或"Eject"
            verbs = driveItem.GetType().InvokeMember("Verbs", System.Reflection.BindingFlags.InvokeMethod,
                null, driveItem, null)!;
            var verbCollection = (System.Collections.IEnumerable)verbs;
            bool foundEject = false;

            foreach (object? verb in verbCollection)
            {
                try
                {
                    if (verb == null) continue;
                    string verbName = verb.GetType().InvokeMember("Name",
                        System.Reflection.BindingFlags.GetProperty, null, verb, null) as string ?? "";

                    if (verbName.Contains("弹出") || verbName.Equals("E&ject", StringComparison.OrdinalIgnoreCase) ||
                        verbName.Equals("Eject", StringComparison.OrdinalIgnoreCase))
                    {
                        verb.GetType().InvokeMember("DoIt", System.Reflection.BindingFlags.InvokeMethod,
                            null, verb, null);
                        foundEject = true;
                        _logger.LogInformation("Shell 执行动词成功: {Verb}", verbName);
                        break;
                    }
                }
                finally
                {
                    if (verb != null) Marshal.ReleaseComObject(verb);
                }
            }

            if (!foundEject)
            {
                _logger.LogDebug("Shell 未找到'弹出'动词（设备可能不支持安全弹出）: {Drive}", driveLetter);
                return EjectResult.DeviceBusy;
            }

            // ⚠ Shell.InvokeVerb 不会在失败时抛异常，必须验证设备是否真的消失
            var maxWait = TimeSpan.FromSeconds(3);
            var interval = TimeSpan.FromMilliseconds(200);
            var deadline = DateTime.UtcNow + maxWait;

            while (DateTime.UtcNow < deadline)
            {
                if (!DriveStillExists(driveLetter))
                {
                    _logger.LogInformation("{Drive} 已成功从系统中移除。", driveLetter);
                    return EjectResult.Success;
                }
                Thread.Sleep(interval);
            }

            _logger.LogWarning("{Drive} 弹出后盘符仍然存在，实际弹出失败。", driveLetter);
            return EjectResult.DeviceBusy;
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogDebug("Shell 弹出权限不足: {Drive}", driveLetter);
            return EjectResult.AccessDenied;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shell 弹出异常: {Drive}", driveLetter);
            return EjectResult.ApiFailure;
        }
        finally
        {
            // 严格按创建逆序释放 COM 对象
            if (verbs != null) Marshal.ReleaseComObject(verbs);
            if (driveItem != null) Marshal.ReleaseComObject(driveItem);
            if (drivesFolder != null) Marshal.ReleaseComObject(drivesFolder);
            if (shell != null) Marshal.ReleaseComObject(shell);
        }
    }

    /// <summary>
    /// 检查指定盘符是否仍然存在于系统中。
    /// </summary>
    private static bool DriveStillExists(string driveLetter)
    {
        try
        {
            var normalized = VolumeResolver.NormalizeDriveLetter(driveLetter);
            if (string.IsNullOrEmpty(normalized)) return false;

            return DriveInfo.GetDrives()
                .Any(d => d.IsReady &&
                          string.Equals(d.Name.TrimEnd('\\').Trim(), normalized, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false; // 异常时视为已移除
        }
    }

    public void Dispose() => GC.SuppressFinalize(this);

    /// <summary>
    /// 通过 CM_Request_Device_Eject / SetupDi 尝试弹出。
    /// 链路：盘符 → CreateFile → IOCTL_STORAGE_GET_DEVICE_NUMBER → SetupDi 枚举磁盘
    /// → 匹配 DeviceNumber → 取 DevInst → CM_Get_Parent → CM_Request_Device_EjectW。
    /// </summary>
    /// <returns>弹出结果及详情（成功时为空；veto 时包含 vetoType 与 vetoName）。</returns>
    private (EjectResult Result, string Detail) EjectViaCfgMgr32(string driveLetter)
    {
        try
        {
            if (!TryGetVolumeDeviceNumber(driveLetter, out var targetDeviceNumber, out var openError))
            {
                return openError switch
                {
                    2 => (EjectResult.DeviceNotFound, $"卷 {driveLetter} 不存在"),
                    5 => (EjectResult.AccessDenied, $"无法访问卷 {driveLetter}"),
                    _ => (EjectResult.ApiFailure, $"打开卷失败：错误 {openError}")
                };
            }

            if (!TryFindDevInstByDeviceNumber(targetDeviceNumber, out var devInst))
            {
                _logger.LogDebug("CM: 未找到 DeviceNumber={Num} 对应的设备实例。", targetDeviceNumber);
                return (EjectResult.DeviceNotFound, "未找到对应物理磁盘的设备实例");
            }

            // 找父设备（USB 控制器侧的根设备实例），CM_Request_Device_Eject 必须作用于父
            var parentResult = NativeMethods.CM_Get_Parent(out var parentInst, devInst, 0);
            if (parentResult != NativeMethods.CR_SUCCESS)
            {
                _logger.LogWarning("CM_Get_Parent 失败: cr={Cr}", parentResult);
                return (EjectResult.ApiFailure, $"CM_Get_Parent 失败 (CR={parentResult})");
            }

            var vetoName = new StringBuilder(NativeMethods.MAX_DEVICE_ID_LEN);
            var ejectResult = NativeMethods.CM_Request_Device_EjectW(
                parentInst,
                out var vetoType,
                vetoName,
                NativeMethods.MAX_DEVICE_ID_LEN,
                0);

            if (ejectResult == NativeMethods.CR_SUCCESS && vetoType == NativeMethods.PNP_VETO_TYPE.Unknown)
            {
                return (EjectResult.Success, string.Empty);
            }

            if (ejectResult == NativeMethods.CR_REMOVE_VETOED || vetoType != NativeMethods.PNP_VETO_TYPE.Unknown)
            {
                var detail = $"被 {vetoType} 阻止" +
                             (vetoName.Length > 0 ? $"（{vetoName}）" : "");
                _logger.LogInformation("CM 弹出被 veto: {Detail}", detail);
                return (EjectResult.DeviceBusyVetoed, detail);
            }

            return (EjectResult.ApiFailure, $"CM_Request_Device_Eject 失败 (CR={ejectResult})");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CM 弹出异常: {Drive}", driveLetter);
            return (EjectResult.ApiFailure, ex.Message);
        }
    }

    /// <summary>
    /// 通过卷句柄 + IOCTL_STORAGE_GET_DEVICE_NUMBER 获取该盘符所属物理磁盘的 DeviceNumber。
    /// </summary>
    private bool TryGetVolumeDeviceNumber(string driveLetter, out int deviceNumber, out int win32Error)
    {
        deviceNumber = -1;
        win32Error = 0;
        var volumePath = $@"\\.\{driveLetter}";

        using var handle = NativeMethods.CreateFile(
            volumePath,
            0, // 不需要读写访问，仅查询信息
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            win32Error = Marshal.GetLastWin32Error();
            _logger.LogDebug("CM: 无法打开卷 {Volume}: 错误 {Error}", volumePath, win32Error);
            return false;
        }

        var ok = NativeMethods.DeviceIoControl(
            handle,
            NativeMethods.IOCTL_STORAGE_GET_DEVICE_NUMBER,
            IntPtr.Zero,
            0,
            out var sdn,
            (uint)Marshal.SizeOf<NativeMethods.STORAGE_DEVICE_NUMBER>(),
            out _,
            IntPtr.Zero);

        if (!ok)
        {
            win32Error = Marshal.GetLastWin32Error();
            _logger.LogDebug("IOCTL_STORAGE_GET_DEVICE_NUMBER 失败: {Error}", win32Error);
            return false;
        }

        deviceNumber = sdn.DeviceNumber;
        return true;
    }

    /// <summary>
    /// 枚举系统所有 disk 接口，找到 DeviceNumber 匹配的 DevInst。
    /// </summary>
    private bool TryFindDevInstByDeviceNumber(int targetDeviceNumber, out uint devInst)
    {
        devInst = 0;
        var diskGuid = NativeMethods.GUID_DEVINTERFACE_DISK;

        var hDevInfo = NativeMethods.SetupDiGetClassDevsW(
            ref diskGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);

        if (hDevInfo == IntPtr.Zero || hDevInfo == new IntPtr(-1))
        {
            _logger.LogWarning("SetupDiGetClassDevs 返回无效句柄。");
            return false;
        }

        try
        {
            var ifData = new NativeMethods.SP_DEVICE_INTERFACE_DATA
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVICE_INTERFACE_DATA>()
            };

            for (uint i = 0; ; i++)
            {
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref diskGuid, i, ref ifData))
                {
                    var err = Marshal.GetLastWin32Error();
                    if (err == NativeMethods.ERROR_NO_MORE_ITEMS) break;
                    _logger.LogDebug("SetupDiEnumDeviceInterfaces 异常退出: {Error}", err);
                    break;
                }

                if (TryMatchDevicePathDeviceNumber(hDevInfo, ref ifData, targetDeviceNumber, out devInst))
                    return true;
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return false;
    }

    /// <summary>
    /// 取得当前接口的设备路径，CreateFile 后查 DeviceNumber 是否匹配。
    /// </summary>
    private bool TryMatchDevicePathDeviceNumber(
        IntPtr hDevInfo,
        ref NativeMethods.SP_DEVICE_INTERFACE_DATA ifData,
        int targetDeviceNumber,
        out uint devInst)
    {
        devInst = 0;

        // 第一次：取 RequiredSize
        var devInfoData = new NativeMethods.SP_DEVINFO_DATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVINFO_DATA>()
        };

        NativeMethods.SetupDiGetDeviceInterfaceDetailW(
            hDevInfo, ref ifData, IntPtr.Zero, 0, out var requiredSize, ref devInfoData);

        if (requiredSize == 0) return false;

        var detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize 在 64 位下为 8（4 字节 cbSize + 2 字节填充对齐到 wchar_t）
            // 在 32 位下为 6。这里用 IntPtr.Size 区分。
            var cbSize = IntPtr.Size == 8 ? 8 : 6;
            Marshal.WriteInt32(detailBuffer, cbSize);

            var ok = NativeMethods.SetupDiGetDeviceInterfaceDetailW(
                hDevInfo, ref ifData, detailBuffer, requiredSize, out _, ref devInfoData);
            if (!ok)
            {
                _logger.LogDebug("SetupDiGetDeviceInterfaceDetail 失败: {Error}", Marshal.GetLastWin32Error());
                return false;
            }

            // DevicePath 紧跟在 cbSize 之后
            var devicePath = Marshal.PtrToStringUni(detailBuffer + 4);
            if (string.IsNullOrEmpty(devicePath)) return false;

            using var diskHandle = NativeMethods.CreateFile(
                devicePath,
                0,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (diskHandle.IsInvalid) return false;

            var ioOk = NativeMethods.DeviceIoControl(
                diskHandle,
                NativeMethods.IOCTL_STORAGE_GET_DEVICE_NUMBER,
                IntPtr.Zero, 0,
                out var sdn,
                (uint)Marshal.SizeOf<NativeMethods.STORAGE_DEVICE_NUMBER>(),
                out _,
                IntPtr.Zero);

            if (!ioOk) return false;

            if (sdn.DeviceNumber == targetDeviceNumber)
            {
                devInst = devInfoData.DevInst;
                return true;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(detailBuffer);
        }

        return false;
    }
}
