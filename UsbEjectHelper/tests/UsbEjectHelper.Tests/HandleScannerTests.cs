using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using UsbEjectHelper.Core;
using Xunit;

namespace UsbEjectHelper.Tests;

/// <summary>
/// HandleScanner 单元测试 —— 验证 PR3 抽象后的接口注入与边界处理。
/// </summary>
public class HandleScannerTests
{
    [Fact]
    public void Scan_InvalidDriveLetter_ShouldReturnInvalidDriveSummary()
    {
        var vr = new Mock<IVolumeResolver>();
        var pi = new Mock<IProcessInspector>();
        var sut = new HandleScanner(vr.Object, pi.Object, NullLogger<HandleScanner>.Instance);

        var summary = sut.Scan("invalid");

        Assert.Equal("invalid", summary.TargetDrive);
        Assert.Single(summary.Results);
        Assert.Equal("InvalidDriveLetter", summary.Results[0].ErrorState);
    }

    [Fact]
    public void Ctor_NullVolumeResolver_ShouldThrow()
    {
        var pi = new Mock<IProcessInspector>();
        Assert.Throws<ArgumentNullException>(() =>
            new HandleScanner(null!, pi.Object, NullLogger<HandleScanner>.Instance));
    }

    [Fact]
    public void Ctor_NullProcessInspector_ShouldThrow()
    {
        var vr = new Mock<IVolumeResolver>();
        Assert.Throws<ArgumentNullException>(() =>
            new HandleScanner(vr.Object, null!, NullLogger<HandleScanner>.Instance));
    }

    [Fact]
    public void Scan_AcceptsCancellationToken_WithoutThrowing()
    {
        var vr = new Mock<IVolumeResolver>();
        var pi = new Mock<IProcessInspector>();
        var sut = new HandleScanner(vr.Object, pi.Object, NullLogger<HandleScanner>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var summary = sut.Scan("E:", cts.Token);
        Assert.NotNull(summary);
    }
}
