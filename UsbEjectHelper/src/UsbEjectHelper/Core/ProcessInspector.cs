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

    /// <summary>风险等级。Critical 永远不可关；High 需打字匹配；Normal 勾选确认即可。</summary>
    public ProcessRiskTier RiskTier { get; init; }

    /// <summary>兼容旧调用：是否为系统关键（Critical 等级）进程。</summary>
    public bool IsCriticalProcess => RiskTier == ProcessRiskTier.Critical;

    /// <summary>风险级别人类可读描述。</summary>
    public string RiskLevel => RiskTier switch
    {
        ProcessRiskTier.Critical => "系统关键进程",
        ProcessRiskTier.High => "高风险进程",
        ProcessRiskTier.Normal => "普通进程",
        _ => "未知"
    };

    /// <summary>UI 是否允许提供"关闭进程"入口。Critical 永远禁止。</summary>
    public bool CanTerminate => RiskTier != ProcessRiskTier.Critical;
}

/// <summary>
/// 进程检查器 —— 查询进程元数据、识别系统关键进程。
/// </summary>
public class ProcessInspector : IProcessInspector, IDisposable
{
    private readonly ILogger<ProcessInspector> _logger;

    /// <summary>
    /// Critical 名单（不分大小写）。这些进程任何关闭操作都被拒绝。
    /// 注意：Process.ProcessName 不带 .exe 后缀，但用户传入也可能带。两种形式都注册。
    /// </summary>
    private static readonly HashSet<string> CriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "System Idle Process", "Idle", "Registry", "MemCompression",
        "csrss", "csrss.exe",
        "wininit", "wininit.exe",
        "winlogon", "winlogon.exe",
        "services", "services.exe",
        "lsass", "lsass.exe",
        "svchost", "svchost.exe",
        "smss", "smss.exe",
        "spoolsv", "spoolsv.exe",
        "dwm", "dwm.exe",
        "audiodg", "audiodg.exe",
        "lsm", "lsm.exe",
        "fontdrvhost", "fontdrvhost.exe"
    };

    /// <summary>
    /// High 名单：操作有显著副作用但不会让系统不可用。强制结束需打字匹配。
    /// </summary>
    private static readonly HashSet<string> HighRiskProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "explorer.exe",
        // Defender / 微软安全
        "MsMpEng", "MsMpEng.exe",
        "NisSrv", "NisSrv.exe",
        "SecurityHealthService", "SecurityHealthService.exe",
        "MpDefenderCoreService", "MpDefenderCoreService.exe",
        // 常见第三方杀软
        "MBAMService", "MBAMService.exe",
        "avp", "avp.exe",
        "ekrn", "ekrn.exe",
        "NortonSecurity", "NortonSecurity.exe",
        "ccSvcHst", "ccSvcHst.exe",
        // 备份 / 同步代理
        "OneDrive", "OneDrive.exe",
        "Dropbox", "Dropbox.exe",
        // 数据库守护
        "mysqld", "mysqld.exe",
        "postgres", "postgres.exe",
        "sqlservr", "sqlservr.exe"
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
                    RiskTier = GetRiskTier(process.ProcessName)
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
    /// 判断进程名是否为系统关键进程（兼容旧 API）。
    /// </summary>
    public static bool IsCriticalProcessName(string processName)
    {
        return CriticalProcesses.Contains(processName);
    }

    /// <summary>
    /// 取进程名对应的风险分级。处理 with/without .exe 两种形式。
    /// 不在任何名单内 → Normal。
    /// </summary>
    public static ProcessRiskTier GetRiskTier(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return ProcessRiskTier.Normal;
        if (CriticalProcesses.Contains(processName)) return ProcessRiskTier.Critical;
        if (HighRiskProcesses.Contains(processName)) return ProcessRiskTier.High;
        return ProcessRiskTier.Normal;
    }

    /// <summary>
    /// 获取系统关键进程名单（只读）。
    /// </summary>
    public static IReadOnlySet<string> GetCriticalProcessNames() => CriticalProcesses;

    /// <summary>
    /// 获取 High 风险进程名单（只读）。
    /// </summary>
    public static IReadOnlySet<string> GetHighRiskProcessNames() => HighRiskProcesses;

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

        return new ProcessInfo
        {
            Pid = process.Id,
            ProcessName = process.ProcessName,
            ExecutablePath = exePath,
            CommandLine = cmdLine,
            RiskTier = GetRiskTier(process.ProcessName)
        };
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
