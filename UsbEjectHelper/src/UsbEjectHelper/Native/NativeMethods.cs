using System.Runtime.InteropServices;

namespace UsbEjectHelper.Core;

/// <summary>
/// Windows Native API P/Invoke 声明集合。
/// </summary>
internal static partial class NativeMethods
{
    private const string Kernel32 = "kernel32.dll";

    /// <summary>
    /// 获取卷的 GUID 路径，如 "\\?\Volume{...}\"
    /// </summary>
    [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetVolumeNameForVolumeMountPoint(
        string lpszVolumeMountPoint,
        System.Text.StringBuilder lpszVolumeName,
        uint cchBufferLength);

    /// <summary>
    /// 查询 DOS 设备名，如 "\Device\HarddiskVolume5"
    /// </summary>
    [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint QueryDosDevice(
        string lpDeviceName,
        System.Text.StringBuilder lpTargetPath,
        uint ucchMax);

    /// <summary>
    /// 获取文件句柄的最终路径。
    /// </summary>
    [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint GetFinalPathNameByHandle(
        IntPtr hFile,
        System.Text.StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);
}
