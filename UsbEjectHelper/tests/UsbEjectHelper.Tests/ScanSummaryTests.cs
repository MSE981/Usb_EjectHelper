using Xunit;
using UsbEjectHelper.Core;

namespace UsbEjectHelper.Tests;

/// <summary>
/// ScanSummary 模型单元测试。
/// </summary>
public class ScanSummaryTests
{
    [Fact]
    public void HasResults_Empty_ShouldBeFalse()
    {
        var summary = new ScanSummary();
        Assert.False(summary.HasResults);
    }

    [Fact]
    public void HasResults_WithResults_ShouldBeTrue()
    {
        var summary = new ScanSummary
        {
            Results = new List<HandleScanResult>
            {
                new() { Pid = 1234, ProcessName = "test.exe" }
            }
        };
        Assert.True(summary.HasResults);
    }

    [Fact]
    public void LimitationNote_WhenEmpty_ShouldContainGuidance()
    {
        var summary = new ScanSummary();
        Assert.NotEmpty(summary.LimitationNote);
        Assert.Contains("Restart Manager", summary.LimitationNote);
        Assert.Contains("便携软件", summary.LimitationNote);
    }

    [Fact]
    public void LimitationNote_WhenHasResults_ShouldBeEmpty()
    {
        var summary = new ScanSummary
        {
            Results = new List<HandleScanResult>
            {
                new() { Pid = 1, ProcessName = "test.exe" }
            }
        };
        Assert.Empty(summary.LimitationNote);
    }

    [Fact]
    public void ScanSummary_DefaultMethod_ShouldBeRestartManager()
    {
        var summary = new ScanSummary();
        Assert.Equal("Restart Manager", summary.Method);
    }
}
