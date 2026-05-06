using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace UsbEjectHelper.Core;

/// <summary>
/// 占用扫描结果模型。
/// </summary>
public class HandleScanResult
{
    /// <summary>进程 ID</summary>
    public int Pid { get; init; }

    /// <summary>进程名称</summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>进程可执行路径</summary>
    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>进程命令行</summary>
    public string CommandLine { get; init; } = string.Empty;

    /// <summary>占用的文件/路径</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>检测方法来源</summary>
    public string DetectionMethod { get; init; } = "Restart Manager";

    /// <summary>是否为系统关键进程</summary>
    public bool IsCriticalProcess { get; init; }

    /// <summary>错误/权限状态（空表示正常）</summary>
    public string ErrorState { get; init; } = string.Empty;
}

/// <summary>
/// 占用扫描结果摘要。
/// </summary>
public class ScanSummary
{
    /// <summary>扫描时间</summary>
    public DateTime ScanTime { get; init; } = DateTime.Now;

    /// <summary>目标盘符</summary>
    public string TargetDrive { get; init; } = string.Empty;

    /// <summary>扫描结果列表</summary>
    public List<HandleScanResult> Results { get; init; } = new();

    /// <summary>使用的扫描方法</summary>
    public string Method { get; init; } = "Restart Manager";

    /// <summary>扫描方法局限性说明（当结果为空时展示）</summary>
    public string LimitationNote => Results.Count == 0
        ? "当前使用 Restart Manager 扫描，仅能发现注册了 RM 资源管理的程序占用。" +
          "便携软件、脚本解释器、命令行当前目录、杀毒扫描等可能无法检测。" +
          "建议：关闭资源管理器窗口、保存文件、切换终端目录后重试；或以管理员模式重新扫描。"
        : string.Empty;

    /// <summary>是否扫描到占用</summary>
    public bool HasResults => Results.Count > 0;
}

/// <summary>
/// 占用句柄扫描器 —— MVP 使用 Restart Manager API 查询占用目标卷的进程。
/// </summary>
public class HandleScanner : IDisposable
{
    private readonly ILogger<HandleScanner> _logger;
    private readonly ILoggerFactory? _ownedFactory;
    private readonly VolumeResolver _volumeResolver;
    private readonly ProcessInspector _processInspector;

    public HandleScanner(VolumeResolver? volumeResolver = null, ProcessInspector? processInspector = null, ILogger<HandleScanner>? logger = null)
    {
        if (logger == null)
        {
            _ownedFactory = LoggerFactory.Create(b => b.AddConsole());
            _logger = _ownedFactory.CreateLogger<HandleScanner>();
        }
        else
        {
            _logger = logger;
        }
        _volumeResolver = volumeResolver ?? new VolumeResolver();
        _processInspector = processInspector ?? new ProcessInspector();
    }

    /// <summary>
    /// 扫描指定盘符的占用情况。
    /// </summary>
    /// <param name="driveLetter">盘符，如 "E:"</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>扫描摘要</returns>
    public ScanSummary Scan(string driveLetter, CancellationToken cancellationToken = default)
    {
        var normalized = VolumeResolver.NormalizeDriveLetter(driveLetter);
        if (string.IsNullOrEmpty(normalized))
        {
            return new ScanSummary
            {
                TargetDrive = driveLetter,
                Results = new List<HandleScanResult>
                {
                    new() { ProcessName = "错误", FilePath = $"无效的盘符：{driveLetter}", ErrorState = "InvalidDriveLetter" }
                }
            };
        }

        _logger.LogInformation("开始扫描 {Drive} 的占用…", normalized);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var summary = new ScanSummary
        {
            TargetDrive = normalized,
            Results = new List<HandleScanResult>()
        };

        try
        {
            var results = ScanViaRestartManager(normalized, cancellationToken);
            summary.Results.AddRange(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restart Manager 扫描异常。");
            summary.Results.Add(new HandleScanResult
            {
                ProcessName = "扫描错误",
                FilePath = ex.Message,
                ErrorState = "ScanException"
            });
        }

        sw.Stop();
        _logger.LogInformation(
            "扫描 {Drive} 完成: {Count} 个占用, 耗时 {Elapsed}ms, 方法={Method}",
            normalized, summary.Results.Count(r => string.IsNullOrEmpty(r.ErrorState)), sw.ElapsedMilliseconds, summary.Method);

        if (!summary.HasResults)
        {
            _logger.LogInformation("扫描为空: Drive={Drive}, Method=RM", normalized);
        }

        return summary;
    }

    /// <summary>
    /// 通过 Restart Manager API 查询占用进程。
    /// </summary>
    private List<HandleScanResult> ScanViaRestartManager(string driveLetter, CancellationToken cancellationToken)
    {
        var results = new List<HandleScanResult>();

        uint sessionHandle = 0;
        try
        {
            // 启动 RM 会话
            var sessionKey = Guid.NewGuid().ToString();
            var result = NativeMethodsRm.RmStartSession(out sessionHandle, 0, sessionKey);
            if (result != 0)
            {
                _logger.LogWarning("RmStartSession 失败: {Result}", result);
                return results;
            }

            // 注册目标资源（盘符路径）
            var resourcePath = driveLetter + "\\";
            result = NativeMethodsRm.RmRegisterResources(
                sessionHandle, 1, new[] { resourcePath }, 0, null, 0, null);
            if (result != 0)
            {
                _logger.LogWarning("RmRegisterResources 失败: {Result}", result);
                NativeMethodsRm.RmEndSession(sessionHandle);
                return results;
            }

            // 查询占用进程列表
            uint procInfoNeeded = 0;
            uint procInfoCount = 0;
            uint rebootReasons = 0;

            result = NativeMethodsRm.RmGetList(
                sessionHandle, out procInfoNeeded, ref procInfoCount, IntPtr.Zero, ref rebootReasons);

            if (result == NativeMethodsRm.ERROR_MORE_DATA && procInfoNeeded > 0)
            {
                // 分配缓冲区并重新查询：将 procInfoCount 设为 procInfoNeeded 以容纳所有条目
                procInfoCount = procInfoNeeded;
                var bufferSize = (int)(procInfoCount * (uint)Marshal.SizeOf<NativeMethodsRm.RM_PROCESS_INFO>());
                var buffer = Marshal.AllocHGlobal(bufferSize);

                try
                {
                    // 二次调用携带足够大的缓冲区
                    result = NativeMethodsRm.RmGetList(
                        sessionHandle, out procInfoNeeded, ref procInfoCount, buffer, ref rebootReasons);

                    if (result == 0) // ERROR_SUCCESS
                    {
                        // 解析 RM_PROCESS_INFO 数组
                        var offset = buffer;
                        for (int i = 0; i < procInfoCount && !cancellationToken.IsCancellationRequested; i++)
                        {
                            var procInfo = Marshal.PtrToStructure<NativeMethodsRm.RM_PROCESS_INFO>(offset);
                            if (procInfo.Process.dwProcessId != 0)
                            {
                                var procResults = BuildResultsFromRmProcess(procInfo);
                                results.AddRange(procResults);
                            }
                            offset += Marshal.SizeOf<NativeMethodsRm.RM_PROCESS_INFO>();
                        }
                    }
                    else
                    {
                        _logger.LogWarning("RmGetList(二次) 失败: {Result}", result);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            else if (result == 0)
            {
                // 无占用进程
                _logger.LogDebug("RmGetList 返回 0 个占用进程。");
            }
            else
            {
                _logger.LogWarning("RmGetList 失败: {Result}", result);
            }

            NativeMethodsRm.RmEndSession(sessionHandle);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Restart Manager 扫描异常");
            if (sessionHandle != 0)
            {
                try { NativeMethodsRm.RmEndSession(sessionHandle); } catch { }
            }
        }

        return results;
    }

    /// <summary>
    /// 从 RM_PROCESS_INFO 构建扫描结果，补充进程元数据。
    /// </summary>
    private List<HandleScanResult> BuildResultsFromRmProcess(NativeMethodsRm.RM_PROCESS_INFO procInfo)
    {
        var results = new List<HandleScanResult>();
        var pid = (int)procInfo.Process.dwProcessId;

        var processInfo = _processInspector.GetProcessInfo(pid);
        var procName = processInfo?.ProcessName ?? procInfo.strAppName ?? $"PID:{pid}";
        var exePath = processInfo?.ExecutablePath ?? "[未知路径]";
        var cmdLine = processInfo?.CommandLine ?? string.Empty;
        var isCritical = processInfo?.IsCriticalProcess ?? false;

        // strAppName 是进程名，strServiceShortName 是服务名（如有）
        var appName = procInfo.strAppName ?? procName;

        results.Add(new HandleScanResult
        {
            Pid = pid,
            ProcessName = appName,
            ExecutablePath = exePath,
            CommandLine = cmdLine,
            FilePath = procInfo.strAppName ?? $"[{appName} 正在使用文件]",
            DetectionMethod = "Restart Manager",
            IsCriticalProcess = isCritical,
            ErrorState = string.Empty
        });

        return results;
    }

    public void Dispose()
    {
        _ownedFactory?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Restart Manager API P/Invoke 声明。
/// </summary>
internal static class NativeMethodsRm
{
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_MORE_DATA = 234;

    // RM_UNIQUE_PROCESS
    [StructLayout(LayoutKind.Sequential)]
    public struct RM_UNIQUE_PROCESS
    {
        public uint dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    // RM_PROCESS_INFO
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;
        public uint ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    public static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    public static extern int RmEndSession(uint dwSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    public static extern int RmRegisterResources(
        uint dwSessionHandle,
        uint nFiles,
        string[] rgsFileNames,
        uint nApplications,
        RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    public static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        IntPtr rgAffectedApps,
        ref uint lpdwRebootReasons);
}
