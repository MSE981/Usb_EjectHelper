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
}
