using UsbEjectHelper.Core;
using Xunit;

namespace UsbEjectHelper.Tests;

/// <summary>
/// ForceEjectService 测试 —— 仅校验 Validate / OpenVolume 两阶段，不在真实 USB 上执行
/// dismount / eject（防止误伤）。
/// </summary>
public class ForceEjectServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("not a drive")]
    [InlineData("@@")]
    public void ForceEject_InvalidDriveLetter_ShouldFailAtValidate(string? drive)
    {
        var svc = new ForceEjectService();
        var result = svc.ForceEject(drive ?? string.Empty);

        Assert.False(result.Success);
        Assert.Equal("Validate", result.Stage);
    }

    [Fact]
    public void ForceEject_SystemDrive_ShouldRefuseAtValidate()
    {
        var svc = new ForceEjectService();
        // C: 一定是系统盘，DriveType=Fixed
        var result = svc.ForceEject("C:");

        Assert.False(result.Success);
        Assert.Equal("Validate", result.Stage);
        Assert.Contains("可移动", result.Reason);
    }

    [Theory]
    [InlineData("c", "C:")]
    [InlineData("C", "C:")]
    [InlineData("c:", "C:")]
    [InlineData("c:\\", "C:")]
    [InlineData("c:/", "C:")]
    [InlineData("  C:\\  ", "C:")]
    public void ForceEject_DriveLetterNormalization_ShouldUppercaseAndStripTrail(string input, string expectedNormalized)
    {
        // 这些都是 C: 的变体，最终都会在 Validate 阶段被拒绝（因为 C: 不是 Removable），
        // 但 result.DriveLetter 应该已经规范化成大写无尾斜杠的形式
        var svc = new ForceEjectService();
        var result = svc.ForceEject(input);
        Assert.False(result.Success);
        Assert.Equal(expectedNormalized, result.DriveLetter);
    }

    [Fact]
    public void ForceEject_NonExistentDrive_ShouldFailAtValidate()
    {
        // Z: 在大多数测试机器上不存在，DriveInfo.DriveType == NoRootDirectory，
        // 不是 Removable → Validate 拒绝
        var svc = new ForceEjectService();
        var result = svc.ForceEject("Z:");
        Assert.False(result.Success);
        Assert.Equal("Validate", result.Stage);
    }
}
