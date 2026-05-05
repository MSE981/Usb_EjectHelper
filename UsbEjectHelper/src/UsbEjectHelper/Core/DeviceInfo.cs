namespace UsbEjectHelper.Core;

/// <summary>
/// 可移动存储设备信息模型。
/// </summary>
public class DeviceInfo
{
    /// <summary>盘符，如 "E:"</summary>
    public string DriveLetter { get; init; } = string.Empty;

    /// <summary>卷标</summary>
    public string VolumeLabel { get; init; } = string.Empty;

    /// <summary>总容量（字节）</summary>
    public long TotalSize { get; init; }

    /// <summary>剩余空间（字节）</summary>
    public long AvailableFreeSpace { get; init; }

    /// <summary>文件系统，如 "NTFS"、"exFAT"</summary>
    public string FileSystem { get; init; } = string.Empty;

    /// <summary>DriveInfo.DriveType 原始值</summary>
    public DriveType DriveType { get; init; }

    /// <summary>卷 GUID 路径，如 "\\?\Volume{...}\"</summary>
    public string VolumeGuid { get; init; } = string.Empty;

    /// <summary>NT 设备路径，如 "\Device\HarddiskVolume5"</summary>
    public string DevicePath { get; init; } = string.Empty;

    /// <summary>是否通过 USB 接口连接</summary>
    public bool IsUsb { get; init; }

    /// <summary>物理磁盘型号</summary>
    public string DiskModel { get; init; } = string.Empty;

    /// <summary>Windows 接口类型，如 "USB"</summary>
    public string InterfaceType { get; init; } = string.Empty;

    /// <summary>是否可弹出（综合判断结果）</summary>
    public bool IsEjectable => DriveType == DriveType.Removable || IsUsb;

    /// <summary>格式化容量显示字符串</summary>
    public string CapacityDisplay
    {
        get
        {
            if (TotalSize <= 0) return "未知";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = TotalSize;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    /// <summary>格式化剩余空间显示字符串</summary>
    public string FreeSpaceDisplay
    {
        get
        {
            if (AvailableFreeSpace <= 0) return "未知";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = AvailableFreeSpace;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    public override string ToString()
        => $"{DriveLetter} [{VolumeLabel}] ({FileSystem}) {CapacityDisplay}";
}
