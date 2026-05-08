using System.Runtime.InteropServices;

namespace UsbEjectHelper.Core;

/// <summary>
/// ntdll.dll + kernel32.dll 句柄枚举相关 P/Invoke。
/// 用于"系统范围句柄扫描"——比 Restart Manager 更可靠地发现持有 U 盘文件 / 目录句柄的进程，
/// 这是 Process Explorer / handle.exe 的实现路径。
/// </summary>
internal static partial class NativeMethods
{
    private const string NtDll = "ntdll.dll";

    public const int SystemExtendedHandleInformation = 64;
    public const int STATUS_SUCCESS = 0;
    public const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

    public const uint PROCESS_DUP_HANDLE = 0x0040;
    public const uint DUPLICATE_SAME_ACCESS = 0x00000002;

    public const uint VOLUME_NAME_DOS = 0x0;
    public const uint FILE_NAME_NORMALIZED = 0x0;

    /// <summary>
    /// 系统范围句柄信息项（SystemExtendedHandleInformation = 64 的条目）。
    /// 64-bit 下 40 字节，无内部填充。
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
    {
        public IntPtr Object;
        public IntPtr UniqueProcessId;
        public IntPtr HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    [DllImport(NtDll)]
    public static extern int NtQuerySystemInformation(
        int SystemInformationClass,
        IntPtr SystemInformation,
        uint SystemInformationLength,
        out uint ReturnLength);

    [DllImport(Kernel32, SetLastError = true)]
    public static extern IntPtr OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        int dwProcessId);

    [DllImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DuplicateHandle(
        IntPtr hSourceProcessHandle,
        IntPtr hSourceHandle,
        IntPtr hTargetProcessHandle,
        out IntPtr lpTargetHandle,
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwOptions);

    [DllImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport(Kernel32)]
    public static extern IntPtr GetCurrentProcess();

    public const uint FILE_TYPE_UNKNOWN = 0x0000;
    public const uint FILE_TYPE_DISK    = 0x0001;
    public const uint FILE_TYPE_CHAR    = 0x0002;
    public const uint FILE_TYPE_PIPE    = 0x0003;
    public const uint FILE_TYPE_REMOTE  = 0x8000;

    /// <summary>查询文件类型（盘 / 字符设备 / 管道）。仅元数据查询，不阻塞 I/O，比对 GetFinalPathNameByHandle 安全。</summary>
    [DllImport(Kernel32, SetLastError = true)]
    public static extern uint GetFileType(IntPtr hFile);
}
