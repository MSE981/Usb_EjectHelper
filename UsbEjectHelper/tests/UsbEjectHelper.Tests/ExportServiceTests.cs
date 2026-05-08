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
        Assert.Contains("Restart Manager", json);
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
}
