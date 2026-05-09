using System.Diagnostics;
using Moq;
using UsbEjectHelper.Core;
using Xunit;

namespace UsbEjectHelper.Tests;

/// <summary>
/// ProcessTerminator 测试。
/// 单元测试用 Mock IProcessInspector；集成测试启动子 cmd.exe 验证真实路径。
/// </summary>
public class ProcessTerminatorTests
{
    private static IProcessTerminator BuildWithInspector(Func<int, ProcessInfo?> getInfo)
    {
        var mock = new Mock<IProcessInspector>();
        mock.Setup(m => m.GetProcessInfo(It.IsAny<int>())).Returns(getInfo);
        return new ProcessTerminator(mock.Object);
    }

    [Fact]
    public void ForceTerminate_CriticalProcess_ShouldRefuse()
    {
        var t = BuildWithInspector(pid => new ProcessInfo
        {
            Pid = pid,
            ProcessName = "csrss.exe",
            RiskTier = ProcessRiskTier.Critical
        });

        var result = t.ForceTerminate(1234);

        Assert.False(result.Success);
        Assert.Equal("Refused-Critical", result.Method);
        Assert.Equal("csrss.exe", result.ProcessName);
    }

    [Fact]
    public void TryCloseGracefully_CriticalProcess_ShouldRefuse()
    {
        var t = BuildWithInspector(pid => new ProcessInfo
        {
            Pid = pid,
            ProcessName = "csrss.exe",
            RiskTier = ProcessRiskTier.Critical
        });

        var result = t.TryCloseGracefully(1234, TimeSpan.FromSeconds(1));

        Assert.False(result.Success);
        Assert.Equal("Refused-Critical", result.Method);
    }

    [Fact]
    public void ForceTerminate_NonExistentProcess_ShouldReturnAlreadyExited()
    {
        var t = BuildWithInspector(_ => null); // 模拟 GetProcessInfo 返回 null
        var result = t.ForceTerminate(999_999_999); // 一个不可能存在的 PID
        Assert.True(result.Success);
        Assert.Equal("AlreadyExited", result.Method);
    }

    [Fact]
    public void ForceTerminate_NegativePid_ShouldFail()
    {
        var t = BuildWithInspector(_ => null);
        var result = t.ForceTerminate(-1);
        Assert.False(result.Success);
        Assert.Equal("Failed-Unknown", result.Method);
    }

    /// <summary>
    /// 集成：启动子 cmd.exe，强制结束，确认它退出。
    /// </summary>
    [Fact]
    public void ForceTerminate_Integration_KillsChildCmd()
    {
        // 启动一个不会自己退的 cmd（用 ping 占着）
        var psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 60 > nul")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using var child = Process.Start(psi)!;
        try
        {
            // 等启动完
            Thread.Sleep(200);
            Assert.False(child.HasExited);

            var inspector = new ProcessInspector();
            var t = new ProcessTerminator(inspector);
            var result = t.ForceTerminate(child.Id);

            Assert.True(result.Success, $"应当成功，实际：{result.Method} - {result.Reason}");
            Assert.Equal("TerminateProcess", result.Method);
            Assert.True(child.WaitForExit(2_000));
        }
        finally
        {
            if (!child.HasExited) { try { child.Kill(); } catch { } }
        }
    }

    /// <summary>
    /// 集成：cmd.exe 这种控制台进程没有顶层窗口（CreateNoWindow=true 时），
    /// WM_CLOSE 应直接返回 NoWindow。
    /// </summary>
    [Fact]
    public void TryCloseGracefully_NoWindow_ShouldReturnNoWindow()
    {
        var psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 60 > nul")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using var child = Process.Start(psi)!;
        try
        {
            Thread.Sleep(200);
            var inspector = new ProcessInspector();
            var t = new ProcessTerminator(inspector);
            var result = t.TryCloseGracefully(child.Id, TimeSpan.FromMilliseconds(500));

            // 如果 cmd 真有窗口（少数情况），就接受 WM_CLOSE 或 Timeout；常规没窗口情况是 NoWindow
            Assert.Contains(result.Method, new[] { "WM_CLOSE-NoWindow", "WM_CLOSE", "WM_CLOSE-Timeout" });
        }
        finally
        {
            if (!child.HasExited) { try { child.Kill(); } catch { } }
        }
    }

    /// <summary>
    /// 集成：批量关闭包含一个不存在 PID + 一个真实子进程，结果应当对应正确。
    /// </summary>
    [Fact]
    public void CloseManyGracefully_MixedPids_ShouldReturnPerPidResults()
    {
        var psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 60 > nul")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using var child = Process.Start(psi)!;
        try
        {
            Thread.Sleep(200);
            var inspector = new ProcessInspector();
            var t = new ProcessTerminator(inspector);
            var pids = new[] { 999_999_998, child.Id };
            var results = t.CloseManyGracefully(pids, TimeSpan.FromMilliseconds(300));

            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.Method == "AlreadyExited");
            Assert.Contains(results, r => r.Method.StartsWith("WM_CLOSE"));
        }
        finally
        {
            if (!child.HasExited) { try { child.Kill(); } catch { } }
        }
    }

    [Fact]
    public void RevealInExplorer_NonExistent_ShouldReturnFalse()
    {
        var t = BuildWithInspector(_ => null);
        Assert.False(t.RevealInExplorer(999_999_999));
    }
}
