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

    /// <summary>
    /// CollectResourcePaths：在 temp 目录上模拟一个"盘"，验证盘根 + 顶层文件 + 1 级子目录文件都被收集。
    /// 这是 E-2 修复（让 RM 真正能找到 notepad / 资源管理器占用）的核心。
    /// </summary>
    [Fact]
    public void CollectResourcePaths_ShouldIncludeRoot_TopLevelFiles_AndOneLevelDeepFiles()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), "UsbEjectHelperResScan_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDir);
        try
        {
            var topFile = Path.Combine(rootDir, "top.txt");
            File.WriteAllText(topFile, "x");

            var subDir = Path.Combine(rootDir, "sub");
            Directory.CreateDirectory(subDir);
            var deepFile = Path.Combine(subDir, "deep.txt");
            File.WriteAllText(deepFile, "x");

            var rootArg = rootDir + Path.DirectorySeparatorChar;
            var paths = HandleScanner.CollectResourcePaths(rootArg, maxFiles: 256, CancellationToken.None);

            Assert.Contains(rootArg, paths);
            Assert.Contains(topFile, paths);
            Assert.Contains(deepFile, paths);
        }
        finally
        {
            try { Directory.Delete(rootDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CollectResourcePaths_ShouldRespectMaxFilesCap()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), "UsbEjectHelperResCap_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDir);
        try
        {
            for (int i = 0; i < 50; i++)
            {
                File.WriteAllText(Path.Combine(rootDir, $"f{i}.txt"), "x");
            }
            var paths = HandleScanner.CollectResourcePaths(rootDir + "\\", maxFiles: 10, CancellationToken.None);
            Assert.True(paths.Length <= 10, $"应受 maxFiles 限制，实际 {paths.Length}");
        }
        finally
        {
            try { Directory.Delete(rootDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 默认安全模式契约：不传 allowDeepScan，应仅用 Restart Manager，方法名带 "Safe Mode"。
    /// </summary>
    [Fact]
    public void Scan_DefaultMode_ShouldUseSafeMode()
    {
        var pi = new ProcessInspector();
        var vr = new VolumeResolver();
        using var sut = new HandleScanner(vr, pi, NullLogger<HandleScanner>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var summary = sut.Scan("Z:", cts.Token);

        // 默认走 RM 安全模式，不应该出现 NT
        Assert.DoesNotContain("NT", summary.Method);
        Assert.Contains("Safe Mode", summary.Method);
    }

    /// <summary>
    /// 深度模式契约：扫描不存在的盘符应快速返回（带超时保护后即使遍历 15 万系统句柄也只需百毫秒级）。
    /// </summary>
    [Fact]
    public void Scan_DeepMode_NonExistentDrive_ShouldReturnQuicklyWithNtMethod()
    {
        var pi = new ProcessInspector();
        var vr = new VolumeResolver();
        using var sut = new HandleScanner(vr, pi, NullLogger<HandleScanner>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var summary = sut.Scan("Z:", allowDeepScan: true, cts.Token);
        sw.Stop();

        Assert.Contains("NT", summary.Method);
        Assert.True(sw.Elapsed.TotalSeconds < 10,
            $"NT 扫描应在 10s 内返回，实际 {sw.Elapsed.TotalSeconds:F1}s");
    }

    /// <summary>
    /// 深度模式集成测试：本进程持有 %TEMP% 的一个文件句柄，扫描该盘应找到本进程 PID。
    /// 这是 E-2 修复的核心契约：用 NtQuerySystemInformation 取代 RM 后，
    /// 普通 FileShare.ReadWrite 打开的文件也能被发现。
    /// </summary>
    [Fact]
    public void Scan_DeepMode_FindsCurrentProcessHoldingFile()
    {
        var pi = new ProcessInspector();
        var vr = new VolumeResolver();
        using var sut = new HandleScanner(vr, pi, NullLogger<HandleScanner>.Instance);

        var tempPath = Path.Combine(Path.GetTempPath(), "UsbEjectNtScan_" + Guid.NewGuid().ToString("N") + ".bin");
        var driveOfTemp = Path.GetPathRoot(tempPath)!.TrimEnd('\\');
        Assert.False(string.IsNullOrEmpty(driveOfTemp));

        FileStream? fs = null;
        try
        {
            fs = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
            fs.Write(new byte[] { 1, 2, 3 }, 0, 3);
            fs.Flush();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var summary = sut.Scan(driveOfTemp, allowDeepScan: true, cts.Token);

            var ourPid = Environment.ProcessId;
            Assert.Contains(summary.Results, r => r.Pid == ourPid);
            Assert.Contains("NT", summary.Method);
        }
        finally
        {
            fs?.Dispose();
            try { File.Delete(tempPath); } catch { }
        }
    }
}
