namespace UsbEjectHelper.Core;

/// <summary>
/// 占用扫描器抽象。
///
/// 两种工作模式：
///   1. 安全模式（默认）：只用 Restart Manager。仅能发现注册了 RM 的应用 / 服务，
///      不会枚举系统全量句柄表，不会跨进程 DuplicateHandle，无任何信息披露顾虑。
///   2. 深度模式（用户在设置里显式开启）：先做 NT 系统级句柄枚举，覆盖普通文件 /
///      目录句柄；与 Process Explorer / handle.exe 实现路径相同，仍是用户态 API，
///      但会读到全系统句柄元数据并对同用户进程做 DuplicateHandle，可能被部分
///      EDR / 杀软按启发式标记。
/// </summary>
public interface IHandleScanner
{
    /// <summary>使用安全模式扫描（仅 RM）。</summary>
    ScanSummary Scan(string driveLetter, CancellationToken cancellationToken = default);

    /// <summary>显式选择是否允许深度扫描（NT 系统级句柄枚举）。</summary>
    /// <param name="allowDeepScan">true=允许 NT 路径；false=只用 RM。</param>
    ScanSummary Scan(string driveLetter, bool allowDeepScan, CancellationToken cancellationToken = default);
}
