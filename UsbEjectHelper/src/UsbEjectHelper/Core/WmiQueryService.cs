using System.Management;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace UsbEjectHelper.Core;

/// <summary>
/// WMI 查询服务实现 —— 通过 System.Management 查询 Win32_* 类。
/// </summary>
public class WmiQueryService : IWmiQueryService, IDisposable
{
    private readonly ILogger<WmiQueryService> _logger;
    private ManagementScope _scope;

    public WmiQueryService(ILogger<WmiQueryService>? logger = null)
    {
        _logger = logger ?? NullLogger<WmiQueryService>.Instance;
        _scope = new ManagementScope(@"\\.\root\cimv2");
        try
        {
            _scope.Connect();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI 连接失败，WMI 查询将不可用。");
        }
    }

    /// <inheritdoc />
    public List<WmiDiskDrive> GetDiskDrives()
    {
        var results = new List<WmiDiskDrive>();
        try
        {
            using var searcher = new ManagementObjectSearcher(_scope,
                new ObjectQuery("SELECT DeviceID, Model, InterfaceType, PNPDeviceID FROM Win32_DiskDrive"));
            foreach (ManagementObject obj in searcher.Get())
            {
                results.Add(new WmiDiskDrive
                {
                    DeviceID = obj["DeviceID"]?.ToString() ?? string.Empty,
                    Model = obj["Model"]?.ToString() ?? string.Empty,
                    InterfaceType = obj["InterfaceType"]?.ToString() ?? string.Empty,
                    PNPDeviceID = obj["PNPDeviceID"]?.ToString() ?? string.Empty
                });
                obj.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查询 Win32_DiskDrive 失败。");
        }
        return results;
    }

    /// <inheritdoc />
    public List<WmiLogicalDisk> GetLogicalDisks()
    {
        var results = new List<WmiLogicalDisk>();
        try
        {
            using var searcher = new ManagementObjectSearcher(_scope,
                new ObjectQuery("SELECT DeviceID, VolumeName, FileSystem, Size, FreeSpace, DriveType FROM Win32_LogicalDisk WHERE DriveType = 2 OR DriveType = 3"));
            foreach (ManagementObject obj in searcher.Get())
            {
                results.Add(new WmiLogicalDisk
                {
                    DeviceID = obj["DeviceID"]?.ToString() ?? string.Empty,
                    VolumeName = obj["VolumeName"]?.ToString() ?? string.Empty,
                    FileSystem = obj["FileSystem"]?.ToString() ?? string.Empty,
                    Size = obj["Size"] != null ? Convert.ToInt64(obj["Size"]) : 0,
                    FreeSpace = obj["FreeSpace"] != null ? Convert.ToInt64(obj["FreeSpace"]) : 0,
                    DriveType = obj["DriveType"] != null ? Convert.ToUInt32(obj["DriveType"]) : 0
                });
                obj.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查询 Win32_LogicalDisk 失败。");
        }
        return results;
    }

    /// <inheritdoc />
    public List<WmiDiskPartitionMapping> GetDiskPartitionMappings()
    {
        var results = new List<WmiDiskPartitionMapping>();
        try
        {
            using var searcher = new ManagementObjectSearcher(_scope,
                new ObjectQuery("SELECT * FROM Win32_DiskDriveToDiskPartition"));
            foreach (ManagementObject obj in searcher.Get())
            {
                results.Add(new WmiDiskPartitionMapping
                {
                    Antecedent = obj["Antecedent"]?.ToString() ?? string.Empty,
                    Dependent = obj["Dependent"]?.ToString() ?? string.Empty
                });
                obj.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查询 Win32_DiskDriveToDiskPartition 失败。");
        }
        return results;
    }

    /// <inheritdoc />
    public List<WmiLogicalDiskPartitionMapping> GetLogicalDiskPartitionMappings()
    {
        var results = new List<WmiLogicalDiskPartitionMapping>();
        try
        {
            using var searcher = new ManagementObjectSearcher(_scope,
                new ObjectQuery("SELECT * FROM Win32_LogicalDiskToPartition"));
            foreach (ManagementObject obj in searcher.Get())
            {
                results.Add(new WmiLogicalDiskPartitionMapping
                {
                    Antecedent = obj["Antecedent"]?.ToString() ?? string.Empty,
                    Dependent = obj["Dependent"]?.ToString() ?? string.Empty
                });
                obj.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查询 Win32_LogicalDiskToPartition 失败。");
        }
        return results;
    }

    public void Dispose()
    {
        _scope = null!;
        GC.SuppressFinalize(this);
    }
}
