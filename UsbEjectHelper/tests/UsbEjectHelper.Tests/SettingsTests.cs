using Xunit;
using UsbEjectHelper.Core;
using UsbEjectHelper.Settings;

namespace UsbEjectHelper.Tests;

/// <summary>
/// AppSettings 和 StartupManager 单元测试。
/// </summary>
public class SettingsTests
{
    [Fact]
    public void AppSettings_Default_ShouldHaveExpectedDefaults()
    {
        var settings = new AppSettings();
        Assert.False(settings.AutoStart);
        Assert.True(settings.MinimizeToTrayOnStart);
        Assert.True(settings.CloseToTray);
        Assert.False(settings.EnablePrivacyMode);
    }

    [Fact]
    public void AppSettings_SaveAndLoad_ShouldRoundTrip()
    {
        var settings = new AppSettings
        {
            AutoStart = true,
            MinimizeToTrayOnStart = false,
            CloseToTray = true,
            EnablePrivacyMode = true
        };

        settings.Save();
        var loaded = AppSettings.Load();

        Assert.Equal(settings.AutoStart, loaded.AutoStart);
        Assert.Equal(settings.MinimizeToTrayOnStart, loaded.MinimizeToTrayOnStart);
        Assert.Equal(settings.CloseToTray, loaded.CloseToTray);
        Assert.Equal(settings.EnablePrivacyMode, loaded.EnablePrivacyMode);
    }

    [Fact]
    public void StartupManager_IsStartupEnabled_ShouldNotThrow()
    {
        var mgr = new StartupManager();
        var exception = Record.Exception(() => mgr.IsStartupEnabled());
        Assert.Null(exception);
    }
}
