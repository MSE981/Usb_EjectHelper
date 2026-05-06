using Xunit;

namespace UsbEjectHelper.Tests;

/// <summary>
/// AppSettings 和 StartupManager 单元测试。
/// </summary>
public class SettingsTests : IDisposable
{
    private readonly string _tempFilePath;

    public SettingsTests()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"UsbEjectHelper_Test_{Guid.NewGuid():N}.json");
        UsbEjectHelper.Settings.AppSettings.OverrideFilePath(_tempFilePath);
    }

    public void Dispose()
    {
        UsbEjectHelper.Settings.AppSettings.OverrideFilePath(null);
        try { File.Delete(_tempFilePath); } catch { }
    }

    [Fact]
    public void AppSettings_Default_ShouldHaveExpectedDefaults()
    {
        var settings = new UsbEjectHelper.Settings.AppSettings();
        Assert.False(settings.AutoStart);
        Assert.True(settings.MinimizeToTrayOnStart);
        Assert.True(settings.CloseToTray);
        Assert.False(settings.EnablePrivacyMode);
    }

    [Fact]
    public void AppSettings_SaveAndLoad_ShouldRoundTrip()
    {
        var settings = new UsbEjectHelper.Settings.AppSettings
        {
            AutoStart = true,
            MinimizeToTrayOnStart = false,
            CloseToTray = true,
            EnablePrivacyMode = true
        };

        settings.Save();
        Assert.True(File.Exists(_tempFilePath), "临时文件应已创建");

        var loaded = UsbEjectHelper.Settings.AppSettings.Load();

        Assert.Equal(settings.AutoStart, loaded.AutoStart);
        Assert.Equal(settings.MinimizeToTrayOnStart, loaded.MinimizeToTrayOnStart);
        Assert.Equal(settings.CloseToTray, loaded.CloseToTray);
        Assert.Equal(settings.EnablePrivacyMode, loaded.EnablePrivacyMode);
    }

    [Fact]
    public void StartupManager_IsStartupEnabled_ShouldNotThrow()
    {
        var mgr = new UsbEjectHelper.Settings.StartupManager();
        var exception = Record.Exception(() => mgr.IsStartupEnabled());
        Assert.Null(exception);
    }
}
