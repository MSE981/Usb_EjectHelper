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
        // 安全模式下空结果应给出"开启深度扫描或提权"的可执行建议
        Assert.Contains("深度扫描", summary.LimitationNote);
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
    public void ScanSummary_DefaultMethod_ShouldBeRestartManagerSafeMode()
    {
        // PR8 起默认安全模式：仅 Restart Manager；NT 句柄枚举只有用户显式开启时才走
        var summary = new ScanSummary();
        Assert.Equal("Restart Manager (Safe Mode)", summary.Method);
    }
}
