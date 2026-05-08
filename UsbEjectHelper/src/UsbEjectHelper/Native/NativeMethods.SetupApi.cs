using System.Runtime.InteropServices;

namespace UsbEjectHelper.Core;

/// <summary>
/// setupapi.dll P/Invoke 声明（设备枚举与接口查询）。
/// </summary>
internal static partial class NativeMethods
{
    private const string SetupApi = "setupapi.dll";

    public const uint DIGCF_PRESENT = 0x00000002;
    public const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    public const int ERROR_NO_MORE_ITEMS = 259;

    /// <summary>磁盘设备接口 GUID。</summary>
    public static readonly Guid GUID_DEVINTERFACE_DISK =
        new("53F56307-B6BF-11D0-94F2-00A0C91EFB8B");

    /// <summary>卷设备接口 GUID。</summary>
    public static readonly Guid GUID_DEVINTERFACE_VOLUME =
        new("53F5630D-B6BF-11D0-94F2-00A0C91EFB8B");

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport(SetupApi, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr SetupDiGetClassDevsW(
        ref Guid ClassGuid,
        IntPtr Enumerator,
        IntPtr hwndParent,
        uint Flags);

    [DllImport(SetupApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr DeviceInfoSet,
        IntPtr DeviceInfoData,
        ref Guid InterfaceClassGuid,
        uint MemberIndex,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    /// <summary>
    /// 查询设备接口详情。第一次调用 detailData=IntPtr.Zero 获取所需大小，
    /// 第二次调用传入分配好的内存（首字节为 cbSize：32 位 6，64 位 8）。
    /// </summary>
    [DllImport(SetupApi, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr DeviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
        IntPtr DeviceInterfaceDetailData,
        uint DeviceInterfaceDetailDataSize,
        out uint RequiredSize,
        ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport(SetupApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);
}
