using System.Runtime.InteropServices;

namespace UsbEjectHelper.Core;

/// <summary>
/// kernel32.dll DeviceIoControl 与存储相关 IOCTL 声明。
/// </summary>
internal static partial class NativeMethods
{
    public const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;

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
}
