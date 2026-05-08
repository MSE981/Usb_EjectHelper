using System.Runtime.InteropServices;
using System.Text;

namespace UsbEjectHelper.Core;

/// <summary>
/// cfgmgr32.dll P/Invoke 声明（即插即用设备管理器）。
/// </summary>
internal static partial class NativeMethods
{
    private const string CfgMgr32 = "cfgmgr32.dll";

    public const int CR_SUCCESS = 0;
    public const int CR_REMOVE_VETOED = 0x17;
    public const int MAX_DEVICE_ID_LEN = 200;

    /// <summary>
    /// CM_Request_Device_Eject 的 PNP veto 类型枚举。
    /// </summary>
    public enum PNP_VETO_TYPE
    {
        Unknown = 0,
        LegacyDevice,
        PendingClose,
        WindowsApp,
        WindowsService,
        OutstandingOpen,
        Device,
        Driver,
        IllegalDeviceRequest,
        InsufficientPower,
        NonDisableable,
        LegacyDriver,
        InsufficientRights
    }

    [DllImport(CfgMgr32, SetLastError = true)]
    public static extern int CM_Get_Parent(
        out uint pdnDevInst,
        uint dnDevInst,
        uint ulFlags);

    [DllImport(CfgMgr32, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int CM_Request_Device_EjectW(
        uint dnDevInst,
        out PNP_VETO_TYPE pVetoType,
        StringBuilder pszVetoName,
        uint ulNameLength,
        uint ulFlags);

    /// <summary>
    /// CM_Request_Device_EjectW 的 IntPtr 重载 —— 避免 StringBuilder marshalling 在
    /// 某些设备路径（含特殊 NT 命名空间字符）下读到缓冲尾部未清零内存的问题。
    /// 调用方负责分配 / 释放 <paramref name="pszVetoName"/>。
    /// </summary>
    [DllImport(CfgMgr32, EntryPoint = "CM_Request_Device_EjectW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int CM_Request_Device_EjectW_Ptr(
        uint dnDevInst,
        out PNP_VETO_TYPE pVetoType,
        IntPtr pszVetoName,
        uint ulNameLength,
        uint ulFlags);
}
