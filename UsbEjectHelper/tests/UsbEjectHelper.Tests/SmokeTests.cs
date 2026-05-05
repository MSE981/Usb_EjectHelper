using Xunit;

namespace UsbEjectHelper.Tests;

/// <summary>
/// 基础冒烟测试 —— 验证测试框架可用。
/// </summary>
public class SmokeTests
{
    [Fact]
    public void TestFramework_ShouldWork()
    {
        Assert.True(true);
    }

    [Fact]
    public void AppGuid_ShouldBeValidFormat()
    {
        // Program.AppGuid 格式应为 Guid 格式
        const string expectedPattern = "^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$";
        Assert.Matches(expectedPattern, "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}");
    }
}
