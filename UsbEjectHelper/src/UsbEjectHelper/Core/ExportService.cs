using System.Text.Json;

namespace UsbEjectHelper.Core;

/// <summary>
/// 导出服务 —— JSON 格式导出设备列表与扫描结果，支持隐私脱敏。
/// </summary>
public class ExportService : IExportService
{
    /// <inheritdoc />
    public string ExportDevices(IEnumerable<DeviceInfo> devices)
    {
        var data = new
        {
            ExportedAt = DateTime.Now.ToString("O"),
            Type = "Devices",
            Devices = devices.Select(d => new
            {
                d.DriveLetter,
                d.VolumeLabel,
                d.FileSystem,
                d.CapacityDisplay,
                d.FreeSpaceDisplay,
                d.DriveType,
                d.IsUsb,
                d.DiskModel,
                d.InterfaceType,
                d.IsEjectable
            })
        };
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <inheritdoc />
    public string ExportScanResults(ScanSummary summary, bool privacyMode = false)
    {
        var data = new
        {
            ExportedAt = DateTime.Now.ToString("O"),
            Type = "ScanResults",
            summary.TargetDrive,
            summary.Method,
            Results = summary.Results.Select(r => new
            {
                r.Pid,
                ProcessName = r.ProcessName,
                ExecutablePath = privacyMode ? SanitizePath(r.ExecutablePath) : r.ExecutablePath,
                FilePath = privacyMode ? SanitizePath(r.FilePath) : r.FilePath,
                r.DetectionMethod,
                r.IsCriticalProcess,
                r.ErrorState
            }),
            HasLimitations = !summary.HasResults,
            summary.LimitationNote
        };
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>路径脱敏 —— 仅保留盘符根和文件名。</summary>
    private static string SanitizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            var fileName = Path.GetFileName(path);
            var root = Path.GetPathRoot(path);
            return root != null ? Path.Combine(root, "...", fileName) : fileName;
        }
        catch
        {
            return "[脱敏]";
        }
    }
}
