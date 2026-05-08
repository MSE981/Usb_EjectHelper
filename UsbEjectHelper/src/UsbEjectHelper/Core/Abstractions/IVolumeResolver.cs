namespace UsbEjectHelper.Core;

/// <summary>
/// 卷路径解析器抽象 —— 盘符 ↔ NT 设备路径 ↔ 卷 GUID 的映射与规范化。
/// </summary>
public interface IVolumeResolver
{
    /// <summary>重建盘符映射缓存。</summary>
    void BuildMappings();

    /// <summary>将任意路径或 NT 设备路径规范化到盘符路径。</summary>
    string NormalizeToDrivePath(string? path);

    /// <summary>盘符 → NT 设备路径，如 "E:" → "\Device\HarddiskVolume5"。</summary>
    string? GetDevicePath(string driveLetter);

    /// <summary>盘符 → 卷 GUID 路径，如 "E:" → "\\?\Volume{...}"。</summary>
    string? GetVolumeGuid(string driveLetter);

    /// <summary>NT 设备路径 → 盘符。</summary>
    string? GetDriveLetterFromDevicePath(string devicePath);

    /// <summary>卷 GUID → 盘符。</summary>
    string? GetDriveLetterFromVolumeGuid(string volumeGuid);

    /// <summary>判断指定盘符是否已映射。</summary>
    bool HasMapping(string driveLetter);

    /// <summary>获取所有已映射的盘符。</summary>
    IReadOnlyCollection<string> GetMappedDrives();
}
