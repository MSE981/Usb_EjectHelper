namespace UsbEjectHelper.Core;

/// <summary>
/// WMI 查询服务抽象 —— 便于单元测试 mock。
/// </summary>
public interface IWmiQueryService
{
    /// <summary>
    /// 查询 Win32_DiskDrive 信息。
    /// </summary>
    List<WmiDiskDrive> GetDiskDrives();

    /// <summary>
    /// 查询 Win32_LogicalDisk 信息。
    /// </summary>
    List<WmiLogicalDisk> GetLogicalDisks();

    /// <summary>
    /// 查询 Win32_DiskDriveToDiskPartition 关联。
    /// </summary>
    List<WmiDiskPartitionMapping> GetDiskPartitionMappings();

    /// <summary>
    /// 查询 Win32_LogicalDiskToPartition 关联。
    /// </summary>
    List<WmiLogicalDiskPartitionMapping> GetLogicalDiskPartitionMappings();
}

/// <summary>WMI Win32_DiskDrive DTO。</summary>
public class WmiDiskDrive
{
    public string DeviceID { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string InterfaceType { get; init; } = string.Empty;
    public string PNPDeviceID { get; init; } = string.Empty;
}

/// <summary>WMI Win32_LogicalDisk DTO。</summary>
public class WmiLogicalDisk
{
    public string DeviceID { get; init; } = string.Empty;
    public string VolumeName { get; init; } = string.Empty;
    public string FileSystem { get; init; } = string.Empty;
    public long Size { get; init; }
    public long FreeSpace { get; init; }
    public uint DriveType { get; init; }
}

/// <summary>WMI Win32_DiskDriveToDiskPartition DTO。</summary>
public class WmiDiskPartitionMapping
{
    public string Antecedent { get; init; } = string.Empty;  // Win32_DiskDrive path
    public string Dependent { get; init; } = string.Empty;   // Win32_DiskPartition path
}

/// <summary>WMI Win32_LogicalDiskToPartition DTO。</summary>
public class WmiLogicalDiskPartitionMapping
{
    public string Antecedent { get; init; } = string.Empty;  // Win32_LogicalDisk path
    public string Dependent { get; init; } = string.Empty;   // Win32_DiskPartition path
}
