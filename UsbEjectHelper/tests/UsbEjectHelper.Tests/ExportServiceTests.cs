using Xunit;
using UsbEjectHelper.Core;

namespace UsbEjectHelper.Tests;

/// <summary>
/// ExportService 导出服务单元测试。
/// </summary>
public class ExportServiceTests
{
    private readonly IExportService _sut = new ExportService();

    [Fact]
    public void ExportDevices_ShouldReturnValidJson()
    {
        var devices = new List<DeviceInfo>
        {
            new() { DriveLetter = "E:", VolumeLabel = "USB_DRIVE", FileSystem = "FAT32", TotalSize = 8L * 1024 * 1024 * 1024 }
        };

        var json = _sut.ExportDevices(devices);
        Assert.NotEmpty(json);
        Assert.Contains("USB_DRIVE", json);
        Assert.Contains("E:", json);
        Assert.Contains("Devices", json);
    }

    [Fact]
    public void ExportDevices_Empty_ShouldStillBeValid()
    {
        var json = _sut.ExportDevices(Array.Empty<DeviceInfo>());
        Assert.NotEmpty(json);
        Assert.Contains("Devices", json);
    }

    [Fact]
    public void ExportScanResults_WithResults_ShouldContainData()
    {
        var summary = new ScanSummary
        {
            TargetDrive = "E:",
            Results = new List<HandleScanResult>
            {
                new() { Pid = 1234, ProcessName = "notepad.exe", FilePath = "E:\\test.txt", DetectionMethod = "Restart Manager" }
            }
        };

        var json = _sut.ExportScanResults(summary);
        Assert.NotEmpty(json);
        Assert.Contains("notepad.exe", json);
        Assert.Contains("1234", json);
        Assert.Contains("ScanResults", json);
    }

    [Fact]
    public void ExportScanResults_Empty_ShouldShowLimitation()
    {
        var summary = new ScanSummary { TargetDrive = "E:" };
        var json = _sut.ExportScanResults(summary);
        Assert.NotEmpty(json);
        // 默认方法字段应当带出来；具体字符串随版本演化（PR8: "NT Handle Scan"）
        Assert.Contains(summary.Method, json);
    }

    [Fact]
    public void ExportScanResults_PrivacyMode_ShouldSanitize()
    {
        var summary = new ScanSummary
        {
            TargetDrive = "E:",
            Results = new List<HandleScanResult>
            {
                new()
                {
                    Pid = 1234,
                    ProcessName = "notepad.exe",
                    ExecutablePath = @"C:\Users\Alice\AppData\Local\app.exe",
                    FilePath = @"E:\Documents\secret.txt",
                    DetectionMethod = "RM"
                }
            }
        };

        var jsonNormal = _sut.ExportScanResults(summary, privacyMode: false);
        var jsonPrivate = _sut.ExportScanResults(summary, privacyMode: true);

        Assert.Contains("Alice", jsonNormal);
        Assert.DoesNotContain("Alice", jsonPrivate);
        Assert.Contains("secret.txt", jsonNormal);
    }

    /// <summary>
    /// 集成测试：把导出 JSON 真正写到磁盘上，验证文件存在、内容可被 JsonDocument 解析、关键字段就位。
    /// 模拟 MainWindow.OnExport 的整条路径（除了 SaveFileDialog）。
    /// </summary>
    [Fact]
    public void ExportToDisk_ShouldProduceParseableJson()
    {
        var summary = new ScanSummary
        {
            TargetDrive = "E:",
            Results = new List<HandleScanResult>
            {
                new() { Pid = 1234, ProcessName = "notepad.exe", FilePath = @"E:\test.txt", DetectionMethod = "Restart Manager" }
            }
        };
        var path = Path.Combine(Path.GetTempPath(), $"UsbEjectHelper_AutoExport_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, _sut.ExportScanResults(summary), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Assert.True(File.Exists(path));
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 0);

            using var doc = System.Text.Json.JsonDocument.Parse(bytes);
            var root = doc.RootElement;
            Assert.Equal("ScanResults", root.GetProperty("Type").GetString());
            Assert.Equal("E:", root.GetProperty("TargetDrive").GetString());
            var first = root.GetProperty("Results").EnumerateArray().First();
            Assert.Equal(1234, first.GetProperty("Pid").GetInt32());
            Assert.Equal("notepad.exe", first.GetProperty("ProcessName").GetString());
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
