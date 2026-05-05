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
public class EjectService
{
    private readonly ILogger<EjectService> _logger;

    public EjectService(ILogger<EjectService>? logger = null)
    {
        _logger = logger ?? LoggerFactory.Create(b => b.AddConsole()).CreateLogger<EjectService>();
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
            // 方法 1: 尝试通过 shell32 弹出
            var shellResult = EjectViaShell(normalized);
            if (shellResult == EjectResult.Success)
            {
                _logger.LogInformation("{Drive} 弹出成功。", normalized);
                return (EjectResult.Success, $"设备 {normalized} 已安全弹出，可以拔出。");
            }

            // 方法 2: 尝试通过 CM_Request_Device_Eject（需要设备实例 ID）
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
    /// 通过 Windows Shell 尝试弹出。
    /// </summary>
    private EjectResult EjectViaShell(string driveLetter)
    {
        try
        {
            // 使用 SHOpenWithDialog / 实际应该用 shell 的弹出方法
            // MVP 使用 Win32 API: 打开卷并尝试 lock/dismount
            var volumePath = $@"\\.\{driveLetter}";

            using var handle = NativeMethodsForEject.CreateFile(
                volumePath,
                NativeMethodsForEject.GENERIC_READ | NativeMethodsForEject.GENERIC_WRITE,
                NativeMethodsForEject.FILE_SHARE_READ | NativeMethodsForEject.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethodsForEject.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogDebug("CreateFile({Path}) 失败: {Error}", volumePath, error);
                return error switch
                {
                    5 => EjectResult.AccessDenied,   // ACCESS_DENIED
                    2 => EjectResult.DeviceNotFound, // FILE_NOT_FOUND
                    _ => EjectResult.DeviceBusy
                };
            }

            // 尝试锁定卷
            if (!NativeMethodsForEject.DeviceIoControl(
                    handle,
                    NativeMethodsForEject.FSCTL_LOCK_VOLUME,
                    IntPtr.Zero, 0,
                    IntPtr.Zero, 0,
                    out _, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogDebug("FSCTL_LOCK_VOLUME 失败: {Error}", error);
                handle.Close();
                return EjectResult.DeviceBusy;
            }

            // 尝试卸载卷
            if (!NativeMethodsForEject.DeviceIoControl(
                    handle,
                    NativeMethodsForEject.FSCTL_DISMOUNT_VOLUME,
                    IntPtr.Zero, 0,
                    IntPtr.Zero, 0,
                    out _, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogDebug("FSCTL_DISMOUNT_VOLUME 失败: {Error}", error);
                // 解锁并返回
                NativeMethodsForEject.DeviceIoControl(
                    handle, NativeMethodsForEject.FSCTL_UNLOCK_VOLUME,
                    IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
                handle.Close();
                return EjectResult.DeviceBusy;
            }

            // 尝试弹出（preventative removal）
            if (!NativeMethodsForEject.DeviceIoControl(
                    handle,
                    NativeMethodsForEject.IOCTL_STORAGE_EJECT_MEDIA,
                    IntPtr.Zero, 0,
                    IntPtr.Zero, 0,
                    out _, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogDebug("IOCTL_STORAGE_EJECT_MEDIA 失败: {Error}", error);
                handle.Close();
                return EjectResult.DeviceBusy;
            }

            handle.Close();
            return EjectResult.Success;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shell 弹出异常");
            return EjectResult.ApiFailure;
        }
    }

    /// <summary>
    /// 通过 cfgmgr32 尝试弹出（预留接口，MVP 若 Shell 方法已够用则跳过）。
    /// </summary>
    private EjectResult EjectViaCfgMgr32(string driveLetter)
    {
        // MVP 阶段：Shell 方法（lock/dismount/eject）已覆盖常见场景。
        // CM_Request_Device_Eject 需要设备实例 ID，后续阶段通过 SetupDi 获取。
        _logger.LogDebug("CM 弹出方法暂未实现（需要设备实例 ID）。");
        return EjectResult.ApiFailure;
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
    public const uint FSCTL_LOCK_VOLUME = 0x00090018;
    public const uint FSCTL_UNLOCK_VOLUME = 0x0009001C;
    public const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;
    public const uint IOCTL_STORAGE_EJECT_MEDIA = 0x002D4808;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(
        Microsoft.Win32.SafeHandles.SafeFileHandle hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize,
        IntPtr lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);
}
