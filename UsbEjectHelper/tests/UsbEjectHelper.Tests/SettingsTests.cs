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
        // 默认不启用深度扫描——安全模式优先
        Assert.False(settings.EnableDeepHandleScan);
    }

    [Fact]
    public void AppSettings_SaveAndLoad_ShouldRoundTrip()
    {
        var settings = new UsbEjectHelper.Settings.AppSettings
        {
            AutoStart = true,
            MinimizeToTrayOnStart = false,
            CloseToTray = true,
            EnablePrivacyMode = true,
            EnableDeepHandleScan = true
        };

        settings.Save();
        Assert.True(File.Exists(_tempFilePath), "临时文件应已创建");

        var loaded = UsbEjectHelper.Settings.AppSettings.Load();

        Assert.Equal(settings.AutoStart, loaded.AutoStart);
        Assert.Equal(settings.MinimizeToTrayOnStart, loaded.MinimizeToTrayOnStart);
        Assert.Equal(settings.CloseToTray, loaded.CloseToTray);
        Assert.Equal(settings.EnablePrivacyMode, loaded.EnablePrivacyMode);
        Assert.Equal(settings.EnableDeepHandleScan, loaded.EnableDeepHandleScan);
    }

    [Fact]
    public void StartupManager_IsStartupEnabled_ShouldNotThrow()
    {
        var mgr = new UsbEjectHelper.Settings.StartupManager();
        var exception = Record.Exception(() => mgr.IsStartupEnabled());
        Assert.Null(exception);
    }

    /// <summary>
    /// 集成测试：用唯一 GUID 值名走真实 HKCU 写入 → 读回 → 删除的完整路径，
    /// 不会与用户现有的 "UsbEjectHelper" Run 项发生碰撞。
    /// </summary>
    [Fact]
    public void StartupManager_EnableThenDisable_RealRegistry_RoundTrips()
    {
        var uniqueValueName = "UsbEjectHelper_AutoTest_" + Guid.NewGuid().ToString("N");
        var mgrType = typeof(UsbEjectHelper.Settings.StartupManager);
        var ctor = mgrType.GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            new[] { typeof(string), typeof(Microsoft.Extensions.Logging.ILogger<UsbEjectHelper.Settings.StartupManager>) });
        Assert.NotNull(ctor);

        var mgr = (UsbEjectHelper.Settings.StartupManager)ctor!.Invoke(new object?[] { uniqueValueName, null });

        Assert.False(mgr.IsStartupEnabled(), "唯一值名启动前不应存在");

        try
        {
            Assert.True(mgr.EnableStartup(), "EnableStartup 应返回 true");
            Assert.True(mgr.IsStartupEnabled(), "EnableStartup 后必须能读回");

            using var key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
            var value = key?.GetValue(uniqueValueName) as string;
            Assert.NotNull(value);
            Assert.StartsWith("\"", value);
            Assert.EndsWith("\"", value);

            Assert.True(mgr.DisableStartup(), "DisableStartup 应返回 true");
            Assert.False(mgr.IsStartupEnabled(), "DisableStartup 后应当读不到");
        }
        finally
        {
            using var cleanup = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            cleanup?.DeleteValue(uniqueValueName, throwOnMissingValue: false);
        }
    }
}
