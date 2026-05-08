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
        // 必须给出可执行的下一步建议（PR8 起从"RM 局限性"改为"提权 / 跨用户提示"）
        Assert.Contains("管理员", summary.LimitationNote);
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
    public void ScanSummary_DefaultMethod_ShouldBeNtHandleScan()
    {
        // PR8 起默认扫描方法升级为 NT Handle Scan，RM 仅作为后备
        var summary = new ScanSummary();
        Assert.Equal("NT Handle Scan", summary.Method);
    }
}
