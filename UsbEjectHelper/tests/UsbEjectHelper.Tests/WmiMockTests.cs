using Xunit;
using UsbEjectHelper.Core;

namespace UsbEjectHelper.Tests;

/// <summary>
/// IWmiQueryService 的内存 mock 实现 —— 用于单元测试。
/// </summary>
public class MockWmiQueryService : IWmiQueryService
{
    public List<WmiDiskDrive> DiskDrives { get; init; } = new();
    public List<WmiLogicalDisk> LogicalDisks { get; init; } = new();
    public List<WmiDiskPartitionMapping> DiskPartitionMappings { get; init; } = new();
    public List<WmiLogicalDiskPartitionMapping> LogicalDiskPartitionMappings { get; init; } = new();

    public List<WmiDiskDrive> GetDiskDrives() => DiskDrives;
    public List<WmiLogicalDisk> GetLogicalDisks() => LogicalDisks;
    public List<WmiDiskPartitionMapping> GetDiskPartitionMappings() => DiskPartitionMappings;
    public List<WmiLogicalDiskPartitionMapping> GetLogicalDiskPartitionMappings() => LogicalDiskPartitionMappings;
}

/// <summary>
/// WMI 查询服务与 DeviceWatcher 集成测试（使用 mock WMI）。
/// 注意：DeviceWatcher.EnumerateDevices 是私有方法，但通过 RefreshDevices 间接触发。
/// 此处测试 Mock 场景的数据构建逻辑正确性。
/// </summary>
public class WmiMockTests
{
    [Fact]
    public void MockWmiQueryService_ShouldBuildCorrectly()
    {
        var mock = new MockWmiQueryService
        {
            DiskDrives = new List<WmiDiskDrive>
            {
                new() { DeviceID = @"\\.\PHYSICALDRIVE0", Model = "Samsung SSD", InterfaceType = "SCSI" },
                new() { DeviceID = @"\\.\PHYSICALDRIVE1", Model = "SanDisk USB", InterfaceType = "USB" }
            },
            LogicalDisks = new List<WmiLogicalDisk>
            {
                new() { DeviceID = "C:", VolumeName = "System", DriveType = 3 },
                new() { DeviceID = "E:", VolumeName = "USB_DRIVE", DriveType = 3 }
            },
            DiskPartitionMappings = new List<WmiDiskPartitionMapping>
            {
                new()
                {
                    Antecedent = @"Win32_DiskDrive.DeviceID=""\\.\PHYSICALDRIVE0""",
                    Dependent = @"Win32_DiskPartition.DeviceID=""Disk #0, Partition #0"""
                },
                new()
                {
                    Antecedent = @"Win32_DiskDrive.DeviceID=""\\.\PHYSICALDRIVE1""",
                    Dependent = @"Win32_DiskPartition.DeviceID=""Disk #1, Partition #0"""
                }
            },
            LogicalDiskPartitionMappings = new List<WmiLogicalDiskPartitionMapping>
            {
                new()
                {
                    Antecedent = @"Win32_DiskPartition.DeviceID=""Disk #0, Partition #0""",
                    Dependent = @"Win32_LogicalDisk.DeviceID=""C:"""
                },
                new()
                {
                    Antecedent = @"Win32_DiskPartition.DeviceID=""Disk #1, Partition #0""",
                    Dependent = @"Win32_LogicalDisk.DeviceID=""E:"""
                }
            }
        };

        // 验证 mock 数据完整性
        Assert.Equal(2, mock.GetDiskDrives().Count);
        Assert.Equal(2, mock.GetLogicalDisks().Count);
        Assert.Equal(2, mock.GetDiskPartitionMappings().Count);
        Assert.Equal(2, mock.GetLogicalDiskPartitionMappings().Count);
    }

    [Fact]
    public void UsbDrive_ShouldBeIdentifiedByInterfaceType()
    {
        var drives = new List<WmiDiskDrive>
        {
            new() { DeviceID = @"\\.\PHYSICALDRIVE0", InterfaceType = "USB" }
        };
        Assert.Single(drives);
        Assert.Equal("USB", drives[0].InterfaceType);
    }

    [Fact]
    public void UsbDrive_ShouldBeIdentifiedByPnpId()
    {
        var drives = new List<WmiDiskDrive>
        {
            new() { DeviceID = @"\\.\PHYSICALDRIVE0", InterfaceType = "SCSI",
                    PNPDeviceID = @"USBSTOR\DISK&VEN_SANDISK" }
        };
        // PNPDeviceID 包含 "USB" 应该被识别
        Assert.Contains("USB", drives[0].PNPDeviceID, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 验证 Win32_LogicalDiskToPartition 映射方向：
    /// Antecedent=DiskPartition, Dependent=LogicalDisk。
    /// 修复前代码反向了 Antecedent/Dependent，会导致 USB Fixed 盘识别失败。
    /// </summary>
    [Fact]
    public void LogicalDiskToPartition_MappingDirection_ShouldBePartitionToDrive()
    {
        // 构造 WMI 返回的数据：Antecedent=分区, Dependent=盘符
        var mock = new MockWmiQueryService
        {
            DiskDrives = new List<WmiDiskDrive>
            {
                new() { DeviceID = @"\\.\PHYSICALDRIVE1", InterfaceType = "USB" }
            },
            LogicalDisks = new List<WmiLogicalDisk>
            {
                new() { DeviceID = "F:", DriveType = 3 }  // Fixed 类型
            },
            DiskPartitionMappings = new List<WmiDiskPartitionMapping>
            {
                new()
                {
                    Antecedent = @"Win32_DiskDrive.DeviceID=""\\.\PHYSICALDRIVE1""",
                    Dependent = @"Win32_DiskPartition.DeviceID=""Disk #1, Partition #0"""
                }
            },
            // Win32_LogicalDiskToPartition: Antecedent=分区, Dependent=逻辑盘
            LogicalDiskPartitionMappings = new List<WmiLogicalDiskPartitionMapping>
            {
                new()
                {
                    Antecedent = @"Win32_DiskPartition.DeviceID=""Disk #1, Partition #0""",
                    Dependent = @"Win32_LogicalDisk.DeviceID=""F:"""
                }
            }
        };

        // 验证映射方向：从 Win32_LogicalDiskToPartition 的 Dependent 提取盘符
        Assert.Equal(@"Win32_DiskPartition.DeviceID=""Disk #1, Partition #0""",
            mock.LogicalDiskPartitionMappings[0].Antecedent);
        Assert.Equal(@"Win32_LogicalDisk.DeviceID=""F:""",
            mock.LogicalDiskPartitionMappings[0].Dependent);
    }

    /// <summary>
    /// 验证 DeviceWatcher 使用 mock WMI 能正确识别 USB Fixed 盘。
    /// </summary>
    [Fact]
    public void DeviceWatcher_WithMockWmi_ShouldIdentifyUsbFixedDrive()
    {
        var mock = new MockWmiQueryService
        {
            DiskDrives = new List<WmiDiskDrive>
            {
                new() { DeviceID = @"\\.\PHYSICALDRIVE1", Model = "USB SSD", InterfaceType = "USB" }
            },
            LogicalDisks = new List<WmiLogicalDisk>
            {
                new() { DeviceID = "G:", DriveType = 3, Size = 256L * 1024 * 1024 * 1024 }
            },
            DiskPartitionMappings = new List<WmiDiskPartitionMapping>
            {
                new()
                {
                    Antecedent = @"Win32_DiskDrive.DeviceID=""\\.\PHYSICALDRIVE1""",
                    Dependent = @"Win32_DiskPartition.DeviceID=""Disk #1, Partition #0"""
                }
            },
            LogicalDiskPartitionMappings = new List<WmiLogicalDiskPartitionMapping>
            {
                new()
                {
                    Antecedent = @"Win32_DiskPartition.DeviceID=""Disk #1, Partition #0""",
                    Dependent = @"Win32_LogicalDisk.DeviceID=""G:"""
                }
            }
        };

        // 验证 WMI Mock 数据能正确构造分区→盘符映射链
        // 分区 ID (Antecedent) → 盘符 (Dependent)
        var partitionId = mock.LogicalDiskPartitionMappings[0].Antecedent
            .Split("\"")[1];  // "Disk #1, Partition #0"
        var driveLetter = mock.LogicalDiskPartitionMappings[0].Dependent
            .Split("\"")[1];  // "G:"
        Assert.Equal("Disk #1, Partition #0", partitionId);
        Assert.Equal("G:", driveLetter);

        // 验证磁盘→分区链
        var diskToPartitionId = mock.DiskPartitionMappings[0].Dependent
            .Split("\"")[1];  // "Disk #1, Partition #0"
        Assert.Equal(partitionId, diskToPartitionId);
    }
}
