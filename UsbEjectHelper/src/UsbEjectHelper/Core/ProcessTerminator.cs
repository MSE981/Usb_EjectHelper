// UsbEjectHelper - Windows tray utility for safely ejecting USB removable storage devices.
// Copyright (C) 2026  Jin Bohan
// Licensed under GNU General Public License v3 or later. See LICENSE.

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static UsbEjectHelper.Core.NativeMethods;

namespace UsbEjectHelper.Core;

/// <summary>
/// 进程关闭实现 —— L1/L2/L4。L3（Restart Manager）在 partial class
/// <c>ProcessTerminator.RestartManager.cs</c> 里。
/// </summary>
public partial class ProcessTerminator : IProcessTerminator
{
    private readonly IProcessInspector _inspector;
    private readonly ILogger<ProcessTerminator> _logger;

    public ProcessTerminator(IProcessInspector inspector, ILogger<ProcessTerminator>? logger = null)
    {
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        _logger = logger ?? NullLogger<ProcessTerminator>.Instance;
    }

    /// <inheritdoc />
    public bool RevealInExplorer(int pid)
    {
        var info = _inspector.GetProcessInfo(pid);
        if (info is null || string.IsNullOrEmpty(info.ExecutablePath) ||
            info.ExecutablePath.StartsWith('['))
        {
            _logger.LogWarning("RevealInExplorer: PID {Pid} 不存在或无可执行路径。", pid);
            return false;
        }

        try
        {
            // /select, 让资源管理器在打开父目录的同时高亮该文件
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{info.ExecutablePath}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RevealInExplorer 失败 PID={Pid}", pid);
            return false;
        }
    }

    /// <inheritdoc />
    public TerminationResult TryCloseGracefully(int pid, TimeSpan timeout, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // 0. 入参校验 + 拿进程
        var precheck = PrecheckForClose(pid, allowCritical: false);
        if (precheck is not null) return precheck with { Duration = sw.Elapsed };

        // PrecheckForClose 返回 null 时，进程仍存在
        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException)
        {
            return BuildResult(pid, "", true, "AlreadyExited", "进程已退出", sw.Elapsed);
        }

        var name = process.ProcessName;

        try
        {
            // 1. 找该进程的所有顶层窗口
            var windows = FindTopLevelWindows((uint)pid);
            if (windows.Count == 0)
            {
                _logger.LogInformation("PID {Pid} ({Name}) 无顶层窗口，无法 WM_CLOSE。", pid, name);
                return BuildResult(pid, name, false, "WM_CLOSE-NoWindow",
                    "进程没有可用的顶层窗口（后台进程 / 服务），WM_CLOSE 不适用", sw.Elapsed);
            }

            // 2. 给每个可见窗口发 WM_CLOSE
            int posted = 0;
            foreach (var hwnd in windows)
            {
                if (PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                    posted++;
            }
            _logger.LogInformation("已向 PID {Pid} ({Name}) 的 {Posted}/{Total} 个窗口发送 WM_CLOSE。",
                pid, name, posted, windows.Count);

            // 3. 等进程退出
            if (process.WaitForExit((int)Math.Max(timeout.TotalMilliseconds, 100)))
            {
                int exitCode = TryGetExitCode(process);
                return BuildResult(pid, name, true, "WM_CLOSE",
                    $"进程在 {sw.ElapsedMilliseconds}ms 内退出", sw.Elapsed, exitCode);
            }
            return BuildResult(pid, name, false, "WM_CLOSE-Timeout",
                "已发送 WM_CLOSE 但进程未在超时时间内退出（应用可能弹了'是否保存'对话框）", sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TryCloseGracefully 异常 PID={Pid}", pid);
            return BuildResult(pid, name, false, "Failed-Unknown", ex.Message, sw.Elapsed);
        }
        finally { process.Dispose(); }
    }

    /// <inheritdoc />
    public TerminationResult ForceTerminate(int pid)
    {
        var sw = Stopwatch.StartNew();

        var precheck = PrecheckForClose(pid, allowCritical: false);
        if (precheck is not null) return precheck with { Duration = sw.Elapsed };

        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException)
        {
            return BuildResult(pid, "", true, "AlreadyExited", "进程已退出", sw.Elapsed);
        }

        var name = process.ProcessName;

        try
        {
            process.Kill(entireProcessTree: false);
            process.WaitForExit(5_000);
            int exitCode = TryGetExitCode(process);
            return BuildResult(pid, name, true, "TerminateProcess",
                $"进程在 {sw.ElapsedMilliseconds}ms 内被强制结束", sw.Elapsed, exitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5 /* ERROR_ACCESS_DENIED */)
        {
            _logger.LogWarning("强制结束 PID {Pid} 被拒绝（可能是 EDR / 高完整性进程）。", pid);
            return BuildResult(pid, name, false, "Failed-AccessDenied",
                "操作被系统或安全软件拒绝。该进程可能是其他用户、高完整性级别或被 EDR 保护。", sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ForceTerminate 异常 PID={Pid}", pid);
            return BuildResult(pid, name, false, "Failed-Unknown", ex.Message, sw.Elapsed);
        }
        finally { process.Dispose(); }
    }

    /// <inheritdoc />
    public IReadOnlyList<TerminationResult> CloseManyGracefully(
        IEnumerable<int> pids, TimeSpan perProcessTimeout, CancellationToken ct = default)
    {
        var results = new List<TerminationResult>();
        foreach (var pid in pids.Distinct())
        {
            if (ct.IsCancellationRequested) break;
            results.Add(TryCloseGracefully(pid, perProcessTimeout, ct));
        }
        return results;
    }

    /// <summary>
    /// 公共预检：进程是否存在、风险等级是否被允许。返回 null 表示可继续。
    /// </summary>
    private TerminationResult? PrecheckForClose(int pid, bool allowCritical)
    {
        if (pid <= 0)
            return BuildResult(pid, "", false, "Failed-Unknown", "无效的 PID", TimeSpan.Zero);

        var info = _inspector.GetProcessInfo(pid);
        if (info is null)
            return BuildResult(pid, "", true, "AlreadyExited", "进程已退出", TimeSpan.Zero);

        if (!allowCritical && info.RiskTier == ProcessRiskTier.Critical)
        {
            _logger.LogInformation("拒绝关闭 Critical 进程 {Name} (PID {Pid})。", info.ProcessName, pid);
            return BuildResult(pid, info.ProcessName, false, "Refused-Critical",
                $"{info.ProcessName} 是系统关键进程，不允许关闭。", TimeSpan.Zero);
        }

        return null;
    }

    private static TerminationResult BuildResult(
        int pid, string name, bool success, string method, string reason,
        TimeSpan duration, int exitCode = 0)
    {
        return new TerminationResult
        {
            Pid = pid,
            ProcessName = name,
            Success = success,
            Method = method,
            Reason = reason,
            ExitCode = exitCode,
            Duration = duration
        };
    }

    private static int TryGetExitCode(Process p)
    {
        try { return p.HasExited ? p.ExitCode : 0; }
        catch { return 0; }
    }

    /// <summary>
    /// 枚举属于指定 PID 的所有顶层窗口（包括不可见的，不可见窗口可能也在响应消息）。
    /// </summary>
    private static List<IntPtr> FindTopLevelWindows(uint pid)
    {
        var hwnds = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner == pid)
                hwnds.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return hwnds;
    }
}
