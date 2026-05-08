namespace UsbEjectHelper.Core;

/// <summary>
/// 导出服务抽象 —— JSON 导出与隐私脱敏。
/// </summary>
public interface IExportService
{
    /// <summary>导出设备列表为 JSON。</summary>
    string ExportDevices(IEnumerable<DeviceInfo> devices);

    /// <summary>导出扫描结果为 JSON；privacyMode=true 时进行路径脱敏。</summary>
    string ExportScanResults(ScanSummary summary, bool privacyMode = false);
}
