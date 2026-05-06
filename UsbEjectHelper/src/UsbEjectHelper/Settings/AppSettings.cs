using System.Text;
using System.Text.Json;

namespace UsbEjectHelper.Settings;

/// <summary>
/// 应用程序设置模型 —— JSON 持久化。
/// </summary>
public class AppSettings
{
    private static string? _settingsFilePathOverride;

    private static string SettingsFilePath =>
        _settingsFilePathOverride ??
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UsbEjectHelper",
            "settings.json");

    /// <summary>开机自启动</summary>
    public bool AutoStart { get; set; }

    /// <summary>启动后最小化到托盘</summary>
    public bool MinimizeToTrayOnStart { get; set; } = true;

    /// <summary>关闭窗口时最小化到托盘（false 则退出）</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>日志脱敏（不记录完整用户路径）</summary>
    public bool EnablePrivacyMode { get; set; }

    /// <summary>单例锁</summary>
    private static readonly object _lock = new();

    /// <summary>
    /// 加载设置，若文件不存在则返回默认值。
    /// </summary>
    public static AppSettings Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                        return settings;
                }
            }
            catch (Exception)
            {
                // 加载失败则使用默认值
            }
            return new AppSettings();
        }
    }

    /// <summary>
    /// 保存当前设置到文件。
    /// </summary>
    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsFilePath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json, Encoding.UTF8);
            }
            catch (Exception)
            {
                // 保存失败静默处理
            }
        }
    }

    /// <summary>
    /// （仅测试用）覆盖设置文件路径，传 null 恢复默认。
    /// </summary>
    public static void OverrideFilePath(string? path)
    {
        _settingsFilePathOverride = path;
    }
}
