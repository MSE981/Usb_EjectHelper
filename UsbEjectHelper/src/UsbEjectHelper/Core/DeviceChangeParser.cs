using System.Runtime.InteropServices;

namespace UsbEjectHelper.Core;

/// <summary>
/// WM_DEVICECHANGE 消息解析辅助 —— 纯函数，方便单元测试。
/// </summary>
public static class DeviceChangeParser
{
    /// <summary>DBT_DEVICEARRIVAL (0x8000)</summary>
    public const int DBT_DEVICEARRIVAL = 0x8000;
    /// <summary>DBT_DEVICEREMOVECOMPLETE (0x8004)</summary>
    public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    /// <summary>DBT_DEVNODES_CHANGED (0x0007)</summary>
    public const int DBT_DEVNODES_CHANGED = 0x0007;
    /// <summary>DBT_DEVTYP_VOLUME (0x0002)</summary>
    public const int DBT_DEVTYP_VOLUME = 0x0002;

    /// <summary>
    /// 从 DEV_BROADCAST_VOLUME 的 dbcv_unitmask 解析盘符集合。
    /// </summary>
    /// <param name="unitMask">位掩码，bit 0 = A:, bit 1 = B:, …</param>
    /// <returns>盘符列表，如 ["E:", "F:"]</returns>
    public static List<string> ParseDriveLettersFromMask(uint unitMask)
    {
        var drives = new List<string>();
        for (int i = 0; i < 26; i++)
        {
            if ((unitMask & (1u << i)) != 0)
            {
                drives.Add($"{(char)('A' + i)}:");
            }
        }
        return drives;
    }

    /// <summary>
    /// 判断一个 WM_DEVICECHANGE 事件是否与卷设备变化相关。
    /// </summary>
    public static bool IsVolumeEvent(int eventType, int deviceType)
        => deviceType == DBT_DEVTYP_VOLUME &&
           (eventType == DBT_DEVICEARRIVAL ||
            eventType == DBT_DEVICEREMOVECOMPLETE);

    /// <summary>
    /// 判断事件类型是否为设备变化（需要刷新的场景）。
    /// </summary>
    public static bool IsRelevantEvent(int eventType)
        => eventType == DBT_DEVICEARRIVAL ||
           eventType == DBT_DEVICEREMOVECOMPLETE ||
           eventType == DBT_DEVNODES_CHANGED;
}
