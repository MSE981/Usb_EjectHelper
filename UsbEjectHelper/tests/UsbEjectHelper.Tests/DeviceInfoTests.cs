using Xunit;
using UsbEjectHelper.Core;

namespace UsbEjectHelper.Tests;

/// <summary>
/// DeviceInfo 模型单元测试。
/// </summary>
public class DeviceInfoTests
{
    [Fact]
    public void IsEjectable_Removable_ShouldBeTrue()
    {
        var device = new DeviceInfo { DriveType = DriveType.Removable, IsUsb = false };
        Assert.True(device.IsEjectable);
    }

    [Fact]
    public void IsEjectable_UsbFixed_ShouldBeTrue()
    {
        var device = new DeviceInfo { DriveType = DriveType.Fixed, IsUsb = true };
        Assert.True(device.IsEjectable);
    }

    [Fact]
    public void IsEjectable_NonUsbFixed_ShouldBeFalse()
    {
        var device = new DeviceInfo { DriveType = DriveType.Fixed, IsUsb = false };
        Assert.False(device.IsEjectable);
    }

    [Fact]
    public void CapacityDisplay_Zero_ShouldReturnUnknown()
    {
        var device = new DeviceInfo { TotalSize = 0 };
        Assert.Equal("未知", device.CapacityDisplay);
    }

    [Fact]
    public void CapacityDisplay_GB_ShouldFormat()
    {
        var device = new DeviceInfo { TotalSize = 16L * 1024 * 1024 * 1024 };
        Assert.Equal("16 GB", device.CapacityDisplay);
    }

    [Fact]
    public void CapacityDisplay_MB_ShouldFormat()
    {
        var device = new DeviceInfo { TotalSize = 512L * 1024 * 1024 };
        Assert.Equal("512 MB", device.CapacityDisplay);
    }
}
