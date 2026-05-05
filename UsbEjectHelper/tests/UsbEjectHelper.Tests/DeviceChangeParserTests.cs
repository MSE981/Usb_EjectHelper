using Xunit;
using UsbEjectHelper.Core;

namespace UsbEjectHelper.Tests;

/// <summary>
/// DeviceChangeParser 纯函数单元测试。
/// </summary>
public class DeviceChangeParserTests
{
    [Fact]
    public void ParseDriveLettersFromMask_SingleDrive_ShouldReturnCorrect()
    {
        // E: = bit 4
        var result = DeviceChangeParser.ParseDriveLettersFromMask(1u << 4);
        Assert.Single(result);
        Assert.Equal("E:", result[0]);
    }

    [Fact]
    public void ParseDriveLettersFromMask_MultipleDrives_ShouldReturnAll()
    {
        // D: (bit 3) + F: (bit 5)
        var result = DeviceChangeParser.ParseDriveLettersFromMask((1u << 3) | (1u << 5));
        Assert.Equal(2, result.Count);
        Assert.Contains("D:", result);
        Assert.Contains("F:", result);
    }

    [Fact]
    public void ParseDriveLettersFromMask_Empty_ShouldReturnEmpty()
    {
        var result = DeviceChangeParser.ParseDriveLettersFromMask(0);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseDriveLettersFromMask_AllDrives_ShouldReturn26()
    {
        // bits 0-25 all set
        uint mask = 0x03FFFFFF;
        var result = DeviceChangeParser.ParseDriveLettersFromMask(mask);
        Assert.Equal(26, result.Count);
    }

    [Fact]
    public void IsVolumeEvent_ValidArrival_ShouldReturnTrue()
    {
        Assert.True(DeviceChangeParser.IsVolumeEvent(
            DeviceChangeParser.DBT_DEVICEARRIVAL,
            DeviceChangeParser.DBT_DEVTYP_VOLUME));
    }

    [Fact]
    public void IsVolumeEvent_ValidRemoval_ShouldReturnTrue()
    {
        Assert.True(DeviceChangeParser.IsVolumeEvent(
            DeviceChangeParser.DBT_DEVICEREMOVECOMPLETE,
            DeviceChangeParser.DBT_DEVTYP_VOLUME));
    }

    [Fact]
    public void IsVolumeEvent_NonVolume_ShouldReturnFalse()
    {
        Assert.False(DeviceChangeParser.IsVolumeEvent(
            DeviceChangeParser.DBT_DEVICEARRIVAL,
            0x0003)); // DBT_DEVTYP_DEVICEINTERFACE
    }

    [Fact]
    public void IsRelevantEvent_ShouldIncludeAllThreeEvents()
    {
        Assert.True(DeviceChangeParser.IsRelevantEvent(DeviceChangeParser.DBT_DEVICEARRIVAL));
        Assert.True(DeviceChangeParser.IsRelevantEvent(DeviceChangeParser.DBT_DEVICEREMOVECOMPLETE));
        Assert.True(DeviceChangeParser.IsRelevantEvent(DeviceChangeParser.DBT_DEVNODES_CHANGED));
    }

    [Fact]
    public void IsRelevantEvent_Unknown_ShouldReturnFalse()
    {
        Assert.False(DeviceChangeParser.IsRelevantEvent(0x9999));
    }
}
