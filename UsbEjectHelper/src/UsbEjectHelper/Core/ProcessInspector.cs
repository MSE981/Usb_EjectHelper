using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace UsbEjectHelper.Core;

/// <summary>
/// 进程信息模型。
/// </summary>
public class ProcessInfo
{
    /// <summary>进程 ID</summary>
    public int Pid { get; init; }

    /// <summary>进程名称</summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>可执行文件完整路径（可能为空）</summary>
    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>命令行（可能为空或截断）</summary>
    public string CommandLine { get; init; } = string.Empty;

    /// <summary>是否为系统关键进程</summary>
    public bool IsCriticalProcess { get; init; }

    /// <summary>风险级别描述</summary>
    public string RiskLevel => IsCriticalProcess ? "系统关键进程" : "普通进程";

    /// <summary>是否允许结束此进程（阶段 1 始终为 false）</summary>
    public bool CanTerminate => false;
}

/// <summary>
/// 进程检查器 —— 查询进程元数据、识别系统关键进程。
/// </summary>
public class ProcessInspector : IDisposable
{
    private readonly ILogger<ProcessInspector> _logger;

    /// <summary>
    /// 系统关键进程名单（不分大小写）。
    /// 这些进程不应被操作，只展示信息。
    /// </summary>
    private static readonly HashSet<string> CriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System",
        "System Idle Process",
        "Idle",
        "csrss.exe",
        "wininit.exe",
        "winlogon.exe",
        "services.exe",
        "lsass.exe",
        "svchost.exe",
        "smss.exe",
        "spoolsv.exe",
        "dwm.exe",
        "audiodg.exe"
    };

    public ProcessInspector(ILogger<ProcessInspector>? logger = null)
    {
        _logger = logger ?? NullLogger<ProcessInspector>.Instance;
    }

    /// <summary>
    /// 获取指定 PID 的进程信息。
    /// </summary>
    /// <param name="pid">进程 ID</param>
    /// <returns>进程信息，或 null（进程已退出/无法访问）</returns>
    public ProcessInfo? GetProcessInfo(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return BuildProcessInfo(process);
        }
        catch (ArgumentException)
        {
            _logger.LogDebug("PID {Pid} 不存在（进程可能已退出）。", pid);
            return null;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogDebug(ex, "无法访问 PID {Pid}（权限不足或受保护进程）。", pid);
            // 权限不足时返回基本信息
            try
            {
                using var process = Process.GetProcessById(pid);
                return new ProcessInfo
                {
                    Pid = pid,
                    ProcessName = process.ProcessName,
                    ExecutablePath = "[权限不足]",
                    CommandLine = "[权限不足]",
                    IsCriticalProcess = IsCriticalProcessName(process.ProcessName)
                };
            }
            catch
            {
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查询 PID {Pid} 信息失败。", pid);
            return null;
        }
    }

    /// <summary>
    /// 批量获取多个 PID 的进程信息。
    /// </summary>
    public List<ProcessInfo> GetProcessInfoBatch(IEnumerable<int> pids)
    {
        var results = new List<ProcessInfo>();
        foreach (var pid in pids.Distinct())
        {
            var info = GetProcessInfo(pid);
            if (info != null)
                results.Add(info);
        }
        return results;
    }

    /// <summary>
    /// 判断进程名是否为系统关键进程。
    /// </summary>
    public static bool IsCriticalProcessName(string processName)
    {
        return CriticalProcesses.Contains(processName);
    }

    /// <summary>
    /// 获取系统关键进程名单（只读）。
    /// </summary>
    public static IReadOnlySet<string> GetCriticalProcessNames() => CriticalProcesses;

    private ProcessInfo BuildProcessInfo(Process process)
    {
        string exePath;
        try { exePath = process.MainModule?.FileName ?? string.Empty; }
        catch { exePath = "[访问被拒绝]"; }

        string cmdLine;
        try
        {
            // 通过 WMI 查询命令行更可靠，但这里先留空以避免性能问题
            cmdLine = string.Empty;
        }
        catch { cmdLine = string.Empty; }

        var isCritical = IsCriticalProcessName(process.ProcessName);

        return new ProcessInfo
        {
            Pid = process.Id,
            ProcessName = process.ProcessName,
            ExecutablePath = exePath,
            CommandLine = cmdLine,
            IsCriticalProcess = isCritical
        };
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
