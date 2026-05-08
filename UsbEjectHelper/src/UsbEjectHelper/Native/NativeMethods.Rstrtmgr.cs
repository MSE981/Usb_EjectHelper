using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace UsbEjectHelper.Core;

/// <summary>
/// rstrtmgr.dll P/Invoke 声明（Restart Manager）。
/// </summary>
internal static partial class NativeMethods
{
    private const string Rstrtmgr = "rstrtmgr.dll";

    public const int ERROR_SUCCESS = 0;
    public const int ERROR_MORE_DATA = 234;

    [StructLayout(LayoutKind.Sequential)]
    public struct RM_UNIQUE_PROCESS
    {
        public uint dwProcessId;
        public FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;
        public uint ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport(Rstrtmgr, CharSet = CharSet.Unicode)]
    public static extern int RmStartSession(
        out uint pSessionHandle,
        int dwSessionFlags,
        string strSessionKey);

    [DllImport(Rstrtmgr, CharSet = CharSet.Unicode)]
    public static extern int RmEndSession(uint dwSessionHandle);

    [DllImport(Rstrtmgr, CharSet = CharSet.Unicode)]
    public static extern int RmRegisterResources(
        uint dwSessionHandle,
        uint nFiles,
        string[] rgsFileNames,
        uint nApplications,
        RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport(Rstrtmgr, CharSet = CharSet.Unicode)]
    public static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        IntPtr rgAffectedApps,
        ref uint lpdwRebootReasons);
}
