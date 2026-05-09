using System.Runtime.InteropServices;

namespace UsbEjectHelper.Core;

/// <summary>
/// kernel32.dll DeviceIoControl 与存储相关 IOCTL 声明。
/// </summary>
internal static partial class NativeMethods
{
    public const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;

    /// <summary>FSCTL_LOCK_VOLUME — 阻止任何新句柄打开该卷；要求当前句柄唯一。</summary>
    public const uint FSCTL_LOCK_VOLUME = 0x00090018;

    /// <summary>FSCTL_DISMOUNT_VOLUME — 卸载卷的文件系统；之后所有现存句柄变 invalid。</summary>
    public const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;

    /// <summary>IOCTL_STORAGE_EJECT_MEDIA — 给设备发"弹出媒体"命令。</summary>
    public const uint IOCTL_STORAGE_EJECT_MEDIA = 0x002D4808;

    [StructLayout(LayoutKind.Sequential)]
    public struct STORAGE_DEVICE_NUMBER
    {
        public int DeviceType;
        public int DeviceNumber;
        public int PartitionNumber;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(
        Microsoft.Win32.SafeHandles.SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        out STORAGE_DEVICE_NUMBER lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    /// <summary>
    /// 不携带 in/out buffer 的 DeviceIoControl，用于 FSCTL_LOCK_VOLUME / FSCTL_DISMOUNT_VOLUME /
    /// IOCTL_STORAGE_EJECT_MEDIA 这类 control-only IOCTL。EntryPoint 指向 DeviceIoControl，
    /// 用不同 C# 方法名避免与上面那个的签名冲突。
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControlSimple(
        Microsoft.Win32.SafeHandles.SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);
}
