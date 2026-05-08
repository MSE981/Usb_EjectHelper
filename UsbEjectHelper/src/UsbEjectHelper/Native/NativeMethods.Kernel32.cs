using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace UsbEjectHelper.Core;

/// <summary>
/// kernel32.dll P/Invoke 声明（卷与文件相关）。
/// </summary>
internal static partial class NativeMethods
{
    private const string Kernel32 = "kernel32.dll";

    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;

    /// <summary>获取卷的 GUID 路径，如 "\\?\Volume{...}\"。</summary>
    [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetVolumeNameForVolumeMountPoint(
        string lpszVolumeMountPoint,
        StringBuilder lpszVolumeName,
        uint cchBufferLength);

    /// <summary>查询 DOS 设备名，如 "\Device\HarddiskVolume5"。</summary>
    [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint QueryDosDevice(
        string lpDeviceName,
        StringBuilder lpTargetPath,
        uint ucchMax);

    /// <summary>获取文件句柄的最终路径。</summary>
    [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint GetFinalPathNameByHandle(
        IntPtr hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    /// <summary>打开文件 / 卷句柄（用于 IOCTL、卷弹出等）。</summary>
    [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);
}
