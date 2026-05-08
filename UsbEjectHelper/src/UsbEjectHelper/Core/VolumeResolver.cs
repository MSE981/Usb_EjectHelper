using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace UsbEjectHelper.Core;

/// <summary>
/// 卷路径解析器 —— 盘符、卷 GUID、NT 设备路径之间的映射与规范化。
/// </summary>
public class VolumeResolver : IVolumeResolver, IDisposable
{
    private readonly ILogger<VolumeResolver> _logger;

    /// <summary>NT 设备名 → 盘符 缓存，如 "\Device\HarddiskVolume5" → "E:"</summary>
    private readonly Dictionary<string, string> _deviceToDriveMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>盘符 → NT 设备名 缓存，如 "E:" → "\Device\HarddiskVolume5"</summary>
    private readonly Dictionary<string, string> _driveToDeviceMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>盘符 → 卷 GUID 缓存，如 "E:" → "\\?\Volume{...}"</summary>
    private readonly Dictionary<string, string> _driveToGuidMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>卷 GUID → 盘符 缓存</summary>
    private readonly Dictionary<string, string> _guidToDriveMap = new(StringComparer.OrdinalIgnoreCase);

    public VolumeResolver(ILogger<VolumeResolver>? logger = null)
    {
        _logger = logger ?? NullLogger<VolumeResolver>.Instance;
        BuildMappings();
    }

    /// <summary>
    /// 构建 NT 设备路径 ↔ 盘符 ↔ 卷 GUID 的映射缓存。
    /// </summary>
    public void BuildMappings()
    {
        _deviceToDriveMap.Clear();
        _driveToDeviceMap.Clear();
        _driveToGuidMap.Clear();
        _guidToDriveMap.Clear();

        var allDrives = DriveInfo.GetDrives();
        foreach (var drive in allDrives)
        {
            var driveLetter = NormalizeDriveLetter(drive.Name);

            // 盘符 → NT 设备路径（如 \Device\HarddiskVolume5）
            var devicePath = QueryDosDevice(driveLetter);
            if (!string.IsNullOrEmpty(devicePath))
            {
                _driveToDeviceMap[driveLetter] = devicePath;
                _deviceToDriveMap[devicePath] = driveLetter;
            }

            // 盘符 → 卷 GUID（如 \\?\Volume{...}）
            var volumeGuid = QueryVolumeGuid(driveLetter);
            if (!string.IsNullOrEmpty(volumeGuid))
            {
                _driveToGuidMap[driveLetter] = volumeGuid;
                _guidToDriveMap[volumeGuid] = driveLetter;
            }
        }

        _logger.LogInformation("卷映射构建完成: {Count} 个盘符已映射。", _driveToDeviceMap.Count);
    }

    /// <summary>
    /// 规范化盘符，确保格式为 "X:"。
    /// </summary>
    /// <param name="driveLetter">输入如 "E:", "E:\", "\\.\E:", "\\?\E:\"</param>
    /// <returns>规范化盘符如 "E:"，或空字符串</returns>
    public static string NormalizeDriveLetter(string? driveLetter)
    {
        if (string.IsNullOrWhiteSpace(driveLetter)) return string.Empty;

        var cleaned = driveLetter.Trim();

        // 去掉 "\\.\" 或 "\\?\" 前缀
        if (cleaned.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[4..];
        else if (cleaned.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[4..];

        // 去掉尾部反斜杠
        cleaned = cleaned.TrimEnd('\\');

        // 检查是否形如 "X:"（大小写不敏感）
        if (cleaned.Length == 2 && char.IsLetter(cleaned[0]) && cleaned[1] == ':')
            return cleaned.ToUpperInvariant();

        return string.Empty;
    }

    /// <summary>
    /// 将任意路径或 NT 设备路径规范化到盘符路径形式。
    /// 例如 "\Device\HarddiskVolume5\folder\file.txt" → "E:\folder\file.txt"
    /// </summary>
    /// <param name="path">原始路径</param>
    /// <returns>规范化后的盘符路径，或原始路径（无法映射时）</returns>
    public string NormalizeToDrivePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        // 已经是盘符路径（如 E:\...）
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            return path.ToUpperInvariant()[0] + path[1..];
        }

        // 可能是 NT 设备路径 \Device\HarddiskVolumeN\...
        foreach (var (devicePath, driveLetter) in _deviceToDriveMap)
        {
            if (path.StartsWith(devicePath, StringComparison.OrdinalIgnoreCase))
            {
                var relative = path[devicePath.Length..];
                if (!relative.StartsWith('\\'))
                    relative = "\\" + relative;
                return driveLetter + relative;
            }
        }

        // 可能是卷 GUID 路径 \\?\Volume{guid}\...
        foreach (var (guidPath, driveLetter) in _guidToDriveMap)
        {
            if (path.StartsWith(guidPath, StringComparison.OrdinalIgnoreCase))
            {
                var relative = path[guidPath.Length..];
                if (!relative.StartsWith('\\'))
                    relative = "\\" + relative;
                return driveLetter + relative;
            }
        }

        // 可能是 \\.\X: 格式
        if (path.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase))
        {
            var cleaned = path[4..];
            if (cleaned.Length >= 2 && char.IsLetter(cleaned[0]) && cleaned[1] == ':')
            {
                return cleaned.ToUpperInvariant()[0] + cleaned[1..];
            }
        }

        // 无法映射，返回原始路径
        return path;
    }

    /// <summary>
    /// 获取盘符对应的 NT 设备路径。
    /// </summary>
    public string? GetDevicePath(string driveLetter)
    {
        var normalized = NormalizeDriveLetter(driveLetter);
        return _driveToDeviceMap.TryGetValue(normalized, out var path) ? path : null;
    }

    /// <summary>
    /// 获取盘符对应的卷 GUID 路径。
    /// </summary>
    public string? GetVolumeGuid(string driveLetter)
    {
        var normalized = NormalizeDriveLetter(driveLetter);
        return _driveToGuidMap.TryGetValue(normalized, out var guid) ? guid : null;
    }

    /// <summary>
    /// 获取 NT 设备路径对应的盘符。
    /// </summary>
    public string? GetDriveLetterFromDevicePath(string devicePath)
    {
        return _deviceToDriveMap.TryGetValue(devicePath, out var drive) ? drive : null;
    }

    /// <summary>
    /// 获取卷 GUID 对应的盘符。
    /// </summary>
    public string? GetDriveLetterFromVolumeGuid(string volumeGuid)
    {
        return _guidToDriveMap.TryGetValue(volumeGuid, out var drive) ? drive : null;
    }

    /// <summary>
    /// 判断指定盘符是否已映射。
    /// </summary>
    public bool HasMapping(string driveLetter)
    {
        var normalized = NormalizeDriveLetter(driveLetter);
        return _driveToDeviceMap.ContainsKey(normalized);
    }

    /// <summary>
    /// 获取所有已映射的盘符。
    /// </summary>
    public IReadOnlyCollection<string> GetMappedDrives() => _driveToDeviceMap.Keys.ToList();

    private static string QueryDosDevice(string driveLetter)
    {
        try
        {
            var sb = new System.Text.StringBuilder(256);
            if (NativeMethods.QueryDosDevice(driveLetter, sb, (uint)sb.Capacity) > 0)
                return sb.ToString();
        }
        catch (Exception) { }
        return string.Empty;
    }

    private static string QueryVolumeGuid(string driveLetter)
    {
        try
        {
            var sb = new System.Text.StringBuilder(128);
            if (NativeMethods.GetVolumeNameForVolumeMountPoint(driveLetter + "\\", sb, (uint)sb.Capacity))
                return sb.ToString().TrimEnd('\\');
        }
        catch (Exception) { }
        return string.Empty;
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
