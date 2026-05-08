using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    public string Method { get; set; } = "Restart Manager (Safe Mode)";

    /// <summary>扫描方法局限性说明（当结果为空时展示）</summary>
    public string LimitationNote => Results.Count == 0
        ? "扫描完成但未发现占用。安全模式下仅能发现注册了 RM 的应用 / 服务（资源管理器、" +
          "Office 等）；如果记事本 / 图片查看器 / 命令行打开的文件没被发现，可在设置里开启" +
          "\"深度扫描\"或以管理员身份重启再试。"
        : string.Empty;

    /// <summary>是否扫描到占用</summary>
    public bool HasResults => Results.Count > 0;
}

/// <summary>
/// 占用句柄扫描器 —— MVP 使用 Restart Manager API 查询占用目标卷的进程。
/// </summary>
public class HandleScanner : IHandleScanner, IDisposable
{
    private readonly ILogger<HandleScanner> _logger;
    private readonly IVolumeResolver _volumeResolver;
    private readonly IProcessInspector _processInspector;

    public HandleScanner(
        IVolumeResolver volumeResolver,
        IProcessInspector processInspector,
        ILogger<HandleScanner>? logger = null)
    {
        _volumeResolver = volumeResolver ?? throw new ArgumentNullException(nameof(volumeResolver));
        _processInspector = processInspector ?? throw new ArgumentNullException(nameof(processInspector));
        _logger = logger ?? NullLogger<HandleScanner>.Instance;
    }

    /// <summary>
    /// 安全模式扫描（仅 Restart Manager）。
    /// 不读系统全量句柄表、不跨进程 DuplicateHandle。无任何信息披露顾虑。
    /// </summary>
    public ScanSummary Scan(string driveLetter, CancellationToken cancellationToken = default) =>
        Scan(driveLetter, allowDeepScan: false, cancellationToken);

    /// <summary>
    /// 显式选择是否允许深度扫描（NT 系统级句柄枚举）。
    /// </summary>
    /// <param name="driveLetter">盘符，如 "E:"</param>
    /// <param name="allowDeepScan">true=允许 NT 路径；false=只用 RM 安全模式。</param>
    /// <param name="cancellationToken">取消令牌</param>
    public ScanSummary Scan(string driveLetter, bool allowDeepScan, CancellationToken cancellationToken = default)
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

        _logger.LogInformation("开始扫描 {Drive} 的占用… 模式={Mode}", normalized,
            allowDeepScan ? "深度（NT 句柄枚举）" : "安全（仅 RM）");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var summary = new ScanSummary
        {
            TargetDrive = normalized,
            Results = new List<HandleScanResult>()
        };

        // 安全模式：完全跳过 NT 路径，只用 Restart Manager。
        // 这是默认行为；用户必须在设置里显式开启深度扫描才走 NT 全量句柄枚举。
        if (!allowDeepScan)
        {
            try
            {
                var rmResults = ScanViaRestartManager(normalized, cancellationToken);
                summary.Results.AddRange(rmResults);
                summary.Method = "Restart Manager (Safe Mode)";
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
            return summary;
        }

        // 深度模式：NT 系统级句柄枚举主路径，RM 兜底。
        bool ntFailed = false;
        try
        {
            var ntResults = ScanViaNtHandles(normalized, cancellationToken);
            summary.Results.AddRange(ntResults);
            summary.Method = "NT Handle Scan";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NT 句柄扫描异常，回退到 Restart Manager。");
            ntFailed = true;
        }

        // RM 的 RmGetList 在系统盘上可能跑数十秒，且取消令牌无法穿透；
        // 仅当 NT 路径自己抛异常时才退回 RM，作为兼容性兜底。
        if (ntFailed && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var rmResults = ScanViaRestartManager(normalized, cancellationToken);
                if (rmResults.Count > 0)
                {
                    foreach (var r in rmResults)
                    {
                        if (!summary.Results.Any(existing => existing.Pid == r.Pid))
                            summary.Results.Add(r);
                    }
                    summary.Method = "Restart Manager (fallback)";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restart Manager 扫描异常。");
                if (summary.Results.Count == 0)
                {
                    summary.Results.Add(new HandleScanResult
                    {
                        ProcessName = "扫描错误",
                        FilePath = ex.Message,
                        ErrorState = "ScanException"
                    });
                }
            }
        }

        sw.Stop();
        _logger.LogInformation(
            "扫描 {Drive} 完成: {Count} 个占用, 耗时 {Elapsed}ms, 方法={Method}",
            normalized, summary.Results.Count(r => string.IsNullOrEmpty(r.ErrorState)), sw.ElapsedMilliseconds, summary.Method);

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
            var result = NativeMethods.RmStartSession(out sessionHandle, 0, sessionKey);
            if (result != 0)
            {
                _logger.LogWarning("RmStartSession 失败: {Result}", result);
                return results;
            }

            // ⚠ 关键：RM 只能匹配持有 *精确资源* 句柄的进程。
            //   仅注册盘根 "E:\" 时，notepad/图片查看器/资源管理器都拿的是
            //   具体文件的句柄而不是盘根，因此一个进程都查不到。
            //   解决：枚举卷上实际文件，连同盘根一并注册（限制数量避免大盘遍历过慢）。
            var resourcePaths = CollectResourcePaths(driveLetter, maxFiles: 256, cancellationToken);
            _logger.LogDebug("RM 注册资源 {Count} 项（含盘根）。", resourcePaths.Length);

            result = NativeMethods.RmRegisterResources(
                sessionHandle,
                (uint)resourcePaths.Length,
                resourcePaths,
                0, null, 0, null);
            if (result != 0)
            {
                _logger.LogWarning("RmRegisterResources 失败: {Result}", result);
                NativeMethods.RmEndSession(sessionHandle);
                return results;
            }

            // 查询占用进程列表
            uint procInfoNeeded = 0;
            uint procInfoCount = 0;
            uint rebootReasons = 0;

            result = NativeMethods.RmGetList(
                sessionHandle, out procInfoNeeded, ref procInfoCount, IntPtr.Zero, ref rebootReasons);

            if (result == NativeMethods.ERROR_MORE_DATA && procInfoNeeded > 0)
            {
                // 分配缓冲区并重新查询：将 procInfoCount 设为 procInfoNeeded 以容纳所有条目
                procInfoCount = procInfoNeeded;
                var bufferSize = (int)(procInfoCount * (uint)Marshal.SizeOf<NativeMethods.RM_PROCESS_INFO>());
                var buffer = Marshal.AllocHGlobal(bufferSize);

                try
                {
                    // 二次调用携带足够大的缓冲区
                    result = NativeMethods.RmGetList(
                        sessionHandle, out procInfoNeeded, ref procInfoCount, buffer, ref rebootReasons);

                    if (result == NativeMethods.ERROR_SUCCESS)
                    {
                        // 解析 RM_PROCESS_INFO 数组
                        var offset = buffer;
                        for (int i = 0; i < procInfoCount && !cancellationToken.IsCancellationRequested; i++)
                        {
                            var procInfo = Marshal.PtrToStructure<NativeMethods.RM_PROCESS_INFO>(offset);
                            if (procInfo.Process.dwProcessId != 0)
                            {
                                var procResults = BuildResultsFromRmProcess(procInfo, driveLetter);
                                results.AddRange(procResults);
                            }
                            offset += Marshal.SizeOf<NativeMethods.RM_PROCESS_INFO>();
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
            else if (result == NativeMethods.ERROR_SUCCESS)
            {
                _logger.LogDebug("RmGetList 返回 0 个占用进程。");
            }
            else
            {
                _logger.LogWarning("RmGetList 失败: {Result}", result);
            }

            NativeMethods.RmEndSession(sessionHandle);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Restart Manager 扫描异常");
            if (sessionHandle != 0)
            {
                try { NativeMethods.RmEndSession(sessionHandle); } catch { }
            }
        }

        return results;
    }

    /// <summary>
    /// 从 RM_PROCESS_INFO 构建扫描结果，补充进程元数据。
    /// </summary>
    private List<HandleScanResult> BuildResultsFromRmProcess(
        NativeMethods.RM_PROCESS_INFO procInfo,
        string driveLetter)
    {
        var results = new List<HandleScanResult>();
        var pid = (int)procInfo.Process.dwProcessId;

        var processInfo = _processInspector.GetProcessInfo(pid);
        var procName = processInfo?.ProcessName ?? procInfo.strAppName ?? $"PID:{pid}";
        var exePath = processInfo?.ExecutablePath ?? "[未知路径]";
        var cmdLine = processInfo?.CommandLine ?? string.Empty;
        var isCritical = processInfo?.IsCriticalProcess ?? false;

        // strAppName 是 RM 给出的友好应用名（例如 "记事本"），strServiceShortName 是服务名
        var appName = !string.IsNullOrEmpty(procInfo.strAppName) ? procInfo.strAppName : procName;

        // RM_PROCESS_INFO 不包含被锁的具体文件名；统一展示"在哪个盘符上持有句柄"
        results.Add(new HandleScanResult
        {
            Pid = pid,
            ProcessName = appName,
            ExecutablePath = exePath,
            CommandLine = cmdLine,
            FilePath = $"在 {driveLetter} 上持有句柄",
            DetectionMethod = "Restart Manager",
            IsCriticalProcess = isCritical,
            ErrorState = string.Empty
        });

        return results;
    }

    // SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX 字段在 x64 下的偏移（结构 40 字节，无填充）
    private const int EntrySize = 40;
    private const int OffsetUniqueProcessId = 8;   // IntPtr (8B)
    private const int OffsetHandleValue = 16;      // IntPtr (8B)
    private const int OffsetGrantedAccess = 24;    // uint   (4B)
    private const int OffsetObjectTypeIndex = 30;  // ushort (2B)

    /// <summary>
    /// Sysinternals 在 handle.exe 里识别为"GetFinalPathNameByHandle / NtQueryObject 可能挂起"
    /// 的访问掩码集合。命名管道、邮件槽等命中这些掩码时跳过名字解析；
    /// 不跳过会导致 134k 句柄的扫描卡死好几分钟。
    /// </summary>
    private static bool IsHangProneAccess(uint access) =>
        access == 0x0012019F ||
        access == 0x001A019F ||
        access == 0x00120189 ||
        access == 0x00100000;

    /// <summary>
    /// 主扫描路径：通过 NtQuerySystemInformation 系统级枚举所有句柄，
    /// 把带有目标盘符前缀的文件 / 目录句柄归并到对应进程上。
    /// 这是 Process Explorer / handle.exe 的工作机制；不依赖目标进程是否注册 RM。
    ///
    /// 性能要点：
    ///   - 不实例化 SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX[]（大盘 100k+ 项时反射成本太高）。
    ///     直接在缓冲里用 Marshal.Read* 按偏移读 ObjectTypeIndex / PID / HandleValue。
    ///   - 先按 ObjectTypeIndex 过滤 File 类型，跳过 Process / Thread / Event / 等等。
    ///   - 每个 PID 仅 OpenProcess 一次（含失败缓存），避免反复打开。
    /// </summary>
    private List<HandleScanResult> ScanViaNtHandles(string driveLetter, CancellationToken cancellationToken)
    {
        var results = new List<HandleScanResult>();
        var drivePrefix1 = $@"\\?\{driveLetter}";        // GetFinalPathNameByHandle(VOLUME_NAME_DOS) 形式
        var drivePrefix2 = $@"\??\{driveLetter}";        // 某些 NT 设备表示形式
        var drivePrefix3 = driveLetter + @"\";           // 兜底

        // 1) 准备一个本进程的"哨兵文件句柄"，用于在枚举里探测 File 类型在当前内核里的 ObjectTypeIndex。
        var sentinelPath = Environment.ProcessPath ?? typeof(HandleScanner).Assembly.Location;
        using var sentinel = NativeMethods.CreateFile(
            sentinelPath,
            NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (sentinel.IsInvalid)
        {
            _logger.LogWarning("无法打开哨兵文件，NT 扫描放弃: {Path}", sentinelPath);
            return results;
        }

        var sentinelHandle = sentinel.DangerousGetHandle();
        var ourPid = Environment.ProcessId;

        // 2) 系统全量句柄枚举（保留缓冲到本方法结束）
        if (!TryQuerySystemHandles(out var buffer, out var count, cancellationToken))
        {
            return results;
        }

        try
        {
            var dataStart = buffer + IntPtr.Size * 2;

            // 3) 第一遍：通过哨兵识别 File 类型索引
            ushort? fileTypeIndex = null;
            for (long i = 0; i < count; i++)
            {
                if ((i & 0x3FFF) == 0 && cancellationToken.IsCancellationRequested) return results;
                var entry = dataStart + (int)(i * EntrySize);
                var pid = Marshal.ReadIntPtr(entry, OffsetUniqueProcessId).ToInt64();
                if ((int)pid != ourPid) continue;
                var handleValue = Marshal.ReadIntPtr(entry, OffsetHandleValue);
                if (handleValue == sentinelHandle)
                {
                    fileTypeIndex = (ushort)Marshal.ReadInt16(entry, OffsetObjectTypeIndex);
                    break;
                }
            }

            if (fileTypeIndex == null)
            {
                _logger.LogWarning("未能在系统句柄表中定位哨兵句柄，NT 扫描跳过。");
                return results;
            }
            _logger.LogInformation("NT 扫描：系统句柄 {Total} 项，File 类型索引 = {Idx}", count, fileTypeIndex.Value);

            // 4) 第二遍：File 类型句柄按命中盘符前缀归并
            //    系统盘上 100k 量级句柄，串行 GetFinalPathNameByHandle 太慢；用 Parallel.ForEach 并行展开。
            //    DuplicateHandle / GetFinalPathNameByHandle 都是线程安全的 Win32 API。
            //    OpenProcess 也是线程安全的，但为避免对同一 PID 反复打开，用 ConcurrentDictionary 做缓存。
            var hitsByPid = new System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Concurrent.ConcurrentBag<string>>();
            var processHandleCache = new System.Collections.Concurrent.ConcurrentDictionary<int, IntPtr>();
            var current = NativeMethods.GetCurrentProcess();

            try
            {
                var po = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8),
                };
                try
                {
                    Parallel.For(0, (int)count, po, i =>
                    {
                        var entry = dataStart + i * EntrySize;
                        var typeIdx = (ushort)Marshal.ReadInt16(entry, OffsetObjectTypeIndex);
                        if (typeIdx != fileTypeIndex.Value) return;

                        var grantedAccess = (uint)Marshal.ReadInt32(entry, OffsetGrantedAccess);
                        if (IsHangProneAccess(grantedAccess)) return; // 命名管道等：跳过名字解析避免挂死

                        var pid = (int)Marshal.ReadIntPtr(entry, OffsetUniqueProcessId).ToInt64();
                        if (pid <= 4) return; // System / Idle

                        var handleValue = Marshal.ReadIntPtr(entry, OffsetHandleValue);

                        if (pid == ourPid)
                        {
                            // 自己进程的句柄不需 dup，但仍必须用超时保护：
                            // 调用方（如 xUnit testhost）可能积累了挂死的 pipe / 异步 I/O 句柄，
                            // 直接 GetFileType / GetFinalPathNameByHandle 会把扫描线程拖死。
                            var ownPath = ResolveWithTimeout(handleValue, timeoutMs: 150,
                                drivePrefix1, drivePrefix2, drivePrefix3);
                            if (!string.IsNullOrEmpty(ownPath) && PathBelongsToDrive(ownPath!, drivePrefix1, drivePrefix2, drivePrefix3))
                            {
                                hitsByPid.GetOrAdd(pid, _ => new()).Add(ownPath!);
                            }
                            return;
                        }

                        var hProc = processHandleCache.GetOrAdd(pid,
                            p => NativeMethods.OpenProcess(NativeMethods.PROCESS_DUP_HANDLE, false, p));
                        if (hProc == IntPtr.Zero) return;

                        if (!NativeMethods.DuplicateHandle(
                                hProc, handleValue, current,
                                out var dupHandle, 0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
                        {
                            return;
                        }

                        // 用专用线程 + 超时来调用 GetFileType / GetFinalPathNameByHandle，
                        // 防止极少数管道 / 异步 I/O 句柄上 API 永久挂死整个工作线程。
                        // Process Explorer / handle.exe 用同样的"工作线程 + TerminateThread 兜底"模式。
                        var path = ResolveWithTimeout(dupHandle,
                            timeoutMs: 150,
                            drivePrefix1, drivePrefix2, drivePrefix3);

                        // 命中才回收 dup；超时句柄交给后台线程，等它自然完成或被 GC（最终通过下面集中关闭兜底）
                        if (path == null)
                        {
                            // 超时，不再 CloseHandle，避免堵在内核
                            return;
                        }
                        try
                        {
                            if (path.Length == 0) return;
                            if (!PathBelongsToDrive(path, drivePrefix1, drivePrefix2, drivePrefix3)) return;
                            hitsByPid.GetOrAdd(pid, _ => new()).Add(path);
                        }
                        finally
                        {
                            NativeMethods.CloseHandle(dupHandle);
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("NT 扫描被取消（超时或用户取消）。");
                }
            }
            finally
            {
                foreach (var hProc in processHandleCache.Values)
                {
                    if (hProc != IntPtr.Zero) NativeMethods.CloseHandle(hProc);
                }
            }

            // 5) 整合结果
            foreach (var kv in hitsByPid)
            {
                var pid = kv.Key;
                var pathsList = kv.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var processInfo = _processInspector.GetProcessInfo(pid);
                var procName = processInfo?.ProcessName ?? $"PID:{pid}";
                var exePath = processInfo?.ExecutablePath ?? "[未知路径]";
                var isCritical = processInfo?.IsCriticalProcess ?? false;
                var firstPath = pathsList[0];
                var sample = pathsList.Count == 1 ? firstPath : $"{firstPath}（共 {pathsList.Count} 个句柄）";

                results.Add(new HandleScanResult
                {
                    Pid = pid,
                    ProcessName = procName,
                    ExecutablePath = exePath,
                    FilePath = sample,
                    DetectionMethod = "NT Handle",
                    IsCriticalProcess = isCritical
                });
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return results;
    }

    private static bool PathBelongsToDrive(string path, string p1, string p2, string p3) =>
        path.StartsWith(p1, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(p2, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(p3, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 调用 NtQuerySystemInformation(SystemExtendedHandleInformation)；缓冲不够则翻倍重试。
    /// 成功时由调用方负责 FreeHGlobal。
    /// </summary>
    private static bool TryQuerySystemHandles(out IntPtr buffer, out long count, CancellationToken ct)
    {
        buffer = IntPtr.Zero;
        count = 0;
        uint length = 0x80000; // 512 KB 起步
        for (int attempt = 0; attempt < 8; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var local = Marshal.AllocHGlobal((int)length);
            var status = NativeMethods.NtQuerySystemInformation(
                NativeMethods.SystemExtendedHandleInformation, local, length, out var returnLength);

            if (status == NativeMethods.STATUS_INFO_LENGTH_MISMATCH)
            {
                Marshal.FreeHGlobal(local);
                length = Math.Max(returnLength + 0x10000, length * 2);
                continue;
            }
            if (status != NativeMethods.STATUS_SUCCESS)
            {
                Marshal.FreeHGlobal(local);
                return false;
            }

            count = Marshal.ReadIntPtr(local).ToInt64();
            if (count <= 0 || count > int.MaxValue)
            {
                Marshal.FreeHGlobal(local);
                return false;
            }
            buffer = local;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 用 GetFinalPathNameByHandle 解析复制过来的句柄路径。
    /// 实测仍可能在管道 / 反应器型句柄上挂死，所以调用方必须用超时包装。
    /// </summary>
    private static string ResolveHandlePath(IntPtr handle)
    {
        var sb = new System.Text.StringBuilder(520);
        var rc = NativeMethods.GetFinalPathNameByHandle(handle, sb, (uint)sb.Capacity, NativeMethods.VOLUME_NAME_DOS);
        if (rc == 0 || rc > sb.Capacity) return string.Empty;
        return sb.ToString();
    }

    /// <summary>
    /// 在专用后台 Thread 上跑 GetFileType + GetFinalPathNameByHandle，超时返回 null（="丢弃此句柄"）。
    ///
    /// 为什么不用 Task.Run + Wait(timeout)：
    ///   Task.Run 借用线程池工作线程；如果被超时丢弃的 Task 实际上仍卡在 Win32 调用里，
    ///   线程池工作线程就再也回不来。多次扫描后线程池被永久占满，整个进程内任何 Task 都被卡住
    ///   （xUnit 用 Task 调度测试），导致测试套件挂死。
    ///
    /// 这里每次新建一个独立 Thread（IsBackground=true，进程退出会自动清理），
    /// 即便它最终卡死也只是泄漏 ~1 个线程，不会污染 ThreadPool。
    /// 这是 Process Explorer / handle.exe 处理同样问题的标准模式。
    /// </summary>
    /// <returns>非空字符串=已解析路径；空字符串=非磁盘 / 解析失败；null=超时已放弃。</returns>
    private static string? ResolveWithTimeout(IntPtr dupHandle, int timeoutMs,
        string drivePrefix1, string drivePrefix2, string drivePrefix3)
    {
        string result = string.Empty;
        var done = new System.Threading.ManualResetEventSlim(initialState: false);
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                if (NativeMethods.GetFileType(dupHandle) != NativeMethods.FILE_TYPE_DISK)
                {
                    result = string.Empty;
                    return;
                }
                result = ResolveHandlePath(dupHandle);
            }
            catch
            {
                result = string.Empty;
            }
            finally
            {
                done.Set();
            }
        })
        {
            IsBackground = true,
            Name = "UsbEjectNtResolve",
        };
        thread.Start();
        return done.Wait(timeoutMs) ? result : null;
    }

    /// <summary>
    /// 收集卷上要交给 RM 注册的资源路径：盘根 + 顶层文件 + 1 级子目录中的文件。
    /// 上限 <paramref name="maxFiles"/> 防止扫描大盘耗时过长。
    /// 公开 internal 仅为单测可见。
    /// </summary>
    internal static string[] CollectResourcePaths(
        string driveLetter,
        int maxFiles,
        CancellationToken cancellationToken)
    {
        var root = driveLetter.EndsWith("\\") ? driveLetter : driveLetter + "\\";
        var paths = new List<string>(capacity: Math.Min(maxFiles, 64))
        {
            root // 资源管理器/CMD 用盘根本身做工作目录的情形
        };

        try
        {
            // 顶层文件
            EnumerateInto(root, paths, maxFiles, cancellationToken);
            if (paths.Count >= maxFiles) return paths.ToArray();

            // 1 级子目录里的文件
            IEnumerable<string> subDirs;
            try { subDirs = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly); }
            catch { return paths.ToArray(); }

            foreach (var dir in subDirs)
            {
                if (cancellationToken.IsCancellationRequested) break;
                EnumerateInto(dir, paths, maxFiles, cancellationToken);
                if (paths.Count >= maxFiles) break;
            }
        }
        catch
        {
            // 忽略权限/IO 错误：至少还有盘根可用
        }

        return paths.ToArray();
    }

    private static void EnumerateInto(
        string dir,
        List<string> paths,
        int maxFiles,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
            {
                if (cancellationToken.IsCancellationRequested) return;
                paths.Add(f);
                if (paths.Count >= maxFiles) return;
            }
        }
        catch { /* 跳过权限/IO 错误目录 */ }
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
