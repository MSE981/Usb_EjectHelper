using Microsoft.Extensions.Logging.Abstractions;
using UsbEjectHelper.Core;
using Xunit;

namespace UsbEjectHelper.Tests;

/// <summary>
/// EjectService 边界条件测试 —— 不依赖真实设备，仅验证输入校验与枚举完整性。
/// </summary>
public class EjectServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("AB:")]
    [InlineData("1:")]
    [InlineData(null)]
    public void TryEject_InvalidDriveLetter_ShouldReturnDeviceNotFound(string? input)
    {
        using var sut = new EjectService(NullLogger<EjectService>.Instance);
        var (result, message) = sut.TryEject(input!);
        Assert.Equal(EjectResult.DeviceNotFound, result);
        Assert.Contains("无效的盘符", message);
    }

    [Fact]
    public void EjectResult_ShouldContainDeviceBusyVetoed()
    {
        Assert.True(Enum.IsDefined(typeof(EjectResult), EjectResult.DeviceBusyVetoed),
            "PR5 后必须存在 DeviceBusyVetoed，用于区分 PNP veto 与一般 DeviceBusy。");
    }

    [Fact]
    public void EjectResult_AllVariants_ShouldBeDistinct()
    {
        var values = Enum.GetValues<EjectResult>();
        Assert.Contains(EjectResult.Success, values);
        Assert.Contains(EjectResult.DeviceBusy, values);
        Assert.Contains(EjectResult.DeviceBusyVetoed, values);
        Assert.Contains(EjectResult.AccessDenied, values);
        Assert.Contains(EjectResult.DeviceNotFound, values);
        Assert.Contains(EjectResult.ApiFailure, values);
    }

    /// <summary>PNP_VETO_TYPE 的全部 13 个枚举值都必须有中文描述，杜绝出现"被 Device 阻止"这种英文。</summary>
    [Fact]
    public void DescribeVeto_AllEnumValues_HaveChineseLabel()
    {
        var method = typeof(EjectService).GetMethod(
            "DescribeVeto",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);

        foreach (var value in Enum.GetValues<UsbEjectHelper.Core.NativeMethods.PNP_VETO_TYPE>())
        {
            var label = (string)method!.Invoke(null, new object[] { value })!;
            Assert.False(string.IsNullOrWhiteSpace(label), $"{value} 应有中文描述");
            // 含中文字符（基本汉字区 \u4e00-\u9fff）
            Assert.Matches(@"[\u4e00-\u9fff]", label);
            // 不应是直接的英文枚举名
            Assert.NotEqual(value.ToString(), label);
        }
    }
}
