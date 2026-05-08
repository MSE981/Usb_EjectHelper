namespace UsbEjectHelper.Core;

/// <summary>
/// 占用扫描器抽象。
/// </summary>
public interface IHandleScanner
{
    /// <summary>扫描指定盘符的占用情况。</summary>
    ScanSummary Scan(string driveLetter, CancellationToken cancellationToken = default);
}
