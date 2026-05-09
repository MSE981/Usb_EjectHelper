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

    /// <summary>
    /// 启用深度占用扫描（NT 系统级句柄枚举）。
    /// 默认 false（仅用 Restart Manager，无任何信息披露顾虑）。
    /// 开启后能发现普通进程持有的文件 / 目录句柄（与 Process Explorer / handle.exe
    /// 同样的实现路径，仍是用户态 API），但会读取系统全量句柄表元数据并对同用户
    /// 进程做 DuplicateHandle，可能被部分 EDR / 杀软按启发式规则标记可疑。
    /// </summary>
    public bool EnableDeepHandleScan { get; set; }

    /// <summary>
    /// 阶段 2 总闸门：是否允许在程序内结束占用进程（L2/L3/L4 任何一种）。
    /// 默认 false。首次开启走二次确认对话框（同 EnableDeepHandleScan 模式）。
    /// </summary>
    public bool AllowProcessTermination { get; set; }

    /// <summary>
    /// L4 单独闸门：是否在 UI 上提供"强制结束 (TerminateProcess)"选项。
    /// 默认 false。开启不会让操作变得自动，每次仍要走 ConfirmTerminateDialog。
    /// </summary>
    public bool EnableForceTerminate { get; set; }

    /// <summary>
    /// 是否允许"强制弹出"按钮（FSCTL_DISMOUNT_VOLUME 路径）。
    /// 默认 false。首次开启走二次确认；每次执行还要走 2s 倒计时确认对话框。
    /// </summary>
    public bool EnableForceEject { get; set; }

    /// <summary>WM_CLOSE 等待进程退出的超时秒数（1~30）。</summary>
    public int GracefulCloseTimeoutSeconds { get; set; } = 5;

    /// <summary>是否记录动作审计日志到 %LOCALAPPDATA%\UsbEjectHelper\actions.log。</summary>
    public bool EnableActionAuditLog { get; set; } = true;

    /// <summary>审计日志单文件大小阈值（MB）。</summary>
    public int AuditLogMaxSizeMB { get; set; } = 1;

    /// <summary>审计日志滚动保留份数。</summary>
    public int AuditLogMaxFiles { get; set; } = 5;

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
