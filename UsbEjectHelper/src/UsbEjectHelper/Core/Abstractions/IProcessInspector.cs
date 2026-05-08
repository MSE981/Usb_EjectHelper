namespace UsbEjectHelper.Core;

/// <summary>
/// 进程检查器抽象。
/// </summary>
public interface IProcessInspector
{
    /// <summary>查询单个进程信息；进程不存在或无法访问时返回 null。</summary>
    ProcessInfo? GetProcessInfo(int pid);

    /// <summary>批量查询进程信息（已去重）。</summary>
    List<ProcessInfo> GetProcessInfoBatch(IEnumerable<int> pids);
}
