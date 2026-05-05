using Xunit;
using UsbEjectHelper.Core;

namespace UsbEjectHelper.Tests;

/// <summary>
/// VolumeResolver 单元测试 —— 路径规范化与映射逻辑。
/// </summary>
public class VolumeResolverTests
{
    [Theory]
    [InlineData("E:", "E:")]
    [InlineData("e:", "E:")]
    [InlineData("E:\\", "E:")]
    [InlineData(@"\\.\E:", "E:")]
    [InlineData(@"\\.\e:", "E:")]
    [InlineData(@"\\?\E:\", "E:")]
    [InlineData("  E:  ", "E:")]
    public void NormalizeDriveLetter_ValidInputs_ShouldReturnCanonical(string input, string expected)
    {
        var result = VolumeResolver.NormalizeDriveLetter(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("AB:")]
    [InlineData("1:")]
    [InlineData("C:\\Windows")]
    public void NormalizeDriveLetter_InvalidInputs_ShouldReturnEmpty(string? input)
    {
        var result = VolumeResolver.NormalizeDriveLetter(input!);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void NormalizeToDrivePath_AlreadyDriveLetter_ShouldNormalize()
    {
        var resolver = new VolumeResolver();
        var result = resolver.NormalizeToDrivePath("e:\\folder\\file.txt");
        Assert.Equal("E:\\folder\\file.txt", result);
    }

    [Fact]
    public void NormalizeToDrivePath_EmptyOrNull_ShouldReturnEmpty()
    {
        var resolver = new VolumeResolver();
        Assert.Equal(string.Empty, resolver.NormalizeToDrivePath(""));
        Assert.Equal(string.Empty, resolver.NormalizeToDrivePath(null));
    }

    [Fact]
    public void NormalizeToDrivePath_DosDevicePrefix_ShouldReturnDrivePath()
    {
        var resolver = new VolumeResolver();
        var result = resolver.NormalizeToDrivePath(@"\\.\E:\folder");
        Assert.Equal("E:\\folder", result);
    }

    [Fact]
    public void HasMapping_AfterBuild_ShouldContainExistingDrives()
    {
        var resolver = new VolumeResolver();
        resolver.BuildMappings();
        // 至少系统盘应该被映射
        var systemDrive = System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:";
        var normalized = VolumeResolver.NormalizeDriveLetter(systemDrive);
        Assert.True(resolver.HasMapping(normalized));
    }

    [Fact]
    public void GetMappedDrives_ShouldReturnNonEmpty()
    {
        var resolver = new VolumeResolver();
        resolver.BuildMappings();
        var drives = resolver.GetMappedDrives();
        Assert.NotEmpty(drives);
    }

    [Fact]
    public void BuildMappings_ShouldNotThrow()
    {
        var resolver = new VolumeResolver();
        var exception = Record.Exception(() => resolver.BuildMappings());
        Assert.Null(exception);
    }
}
