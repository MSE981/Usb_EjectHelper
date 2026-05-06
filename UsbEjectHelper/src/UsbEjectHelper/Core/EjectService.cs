using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace UsbEjectHelper.Core;

/// <summary>
/// 弹出结果状态枚举。
/// </summary>
public enum EjectResult
{
    /// <summary>弹出成功</summary>
    Success,
    /// <summary>设备忙，有占用</summary>
    DeviceBusy,
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
public class EjectService : IDisposable
{
    private readonly ILogger<EjectService> _logger;
    private readonly ILoggerFactory? _ownedFactory;

    public EjectService(ILogger<EjectService>? logger = null)
    {
        if (logger == null)
        {
            _ownedFactory = LoggerFactory.Create(b => b.AddConsole());
            _logger = _ownedFactory.CreateLogger<EjectService>();
        }
        else
        {
            _logger = logger;
        }
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

            // 方法 2: CM_Request_Device_Eject（SetupDi 路径）
            var cmResult = EjectViaCfgMgr32(normalized);
            if (cmResult == EjectResult.Success)
            {
                _logger.LogInformation("{Drive} 通过 CM API 弹出成功。", normalized);
                return (EjectResult.Success, $"设备 {normalized} 已安全弹出，可以拔出。");
            }

            // 两个方法都失败，返回更具体的错误
            _logger.LogWarning("{Drive} 弹出失败: Shell={Shell}, CM={Cm}", normalized, shellResult, cmResult);

            if (shellResult == EjectResult.AccessDenied || cmResult == EjectResult.AccessDenied)
            {
                return (EjectResult.AccessDenied,
                    $"权限不足，无法弹出 {normalized}。请尝试以管理员身份运行。");
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

    public void Dispose()
    {
        _ownedFactory?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 通过 CM_Request_Device_Eject / SetupDi 尝试弹出。
    /// 需要获取设备实例 ID，从盘符反查。
    /// </summary>
    private EjectResult EjectViaCfgMgr32(string driveLetter)
    {
        try
        {
            var volumePath = $@"\\.\{driveLetter}";

            using var handle = NativeMethodsForEject.CreateFile(
                volumePath,
                NativeMethodsForEject.GENERIC_READ,
                NativeMethodsForEject.FILE_SHARE_READ | NativeMethodsForEject.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethodsForEject.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogDebug("CM: 无法打开卷 {Volume}: 错误 {Error}", volumePath, error);
                return error == 2 ? EjectResult.DeviceNotFound :
                       error == 5 ? EjectResult.AccessDenied :
                       EjectResult.ApiFailure;
            }

            // 通过 IOCTL 获取设备号，然后遍历 SetupDi 找匹配设备
            // MVP 简化：调用 CM_Request_Device_Eject 需要 devinst，路径较长
            // 当前 CM 路径作为 Shell 失败后的补充，暂不实现完整链路
            handle.Close();
            _logger.LogDebug("CM_Request_Device_Eject 暂未实现完整链路（Shell 路径为首选）。");
            return EjectResult.ApiFailure;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CM 弹出异常: {Drive}", driveLetter);
            return EjectResult.ApiFailure;
        }
    }
}

/// <summary>
/// 弹出服务使用的 P/Invoke 声明（内部类，避免污染 NativeMethods）。
/// </summary>
internal static class NativeMethodsForEject
{
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);
}
