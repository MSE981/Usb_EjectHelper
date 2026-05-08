using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace UsbEjectHelper.Settings;

/// <summary>
/// 开机自启动管理器 —— 通过 HKCU Run 注册表项控制。
/// </summary>
public class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DefaultValueName = "UsbEjectHelper";

    private readonly ILogger<StartupManager> _logger;
    private readonly string _executablePath;
    private readonly string _valueName;

    public StartupManager(ILogger<StartupManager>? logger = null)
        : this(DefaultValueName, logger) { }

    /// <summary>
    /// 测试 / 集成场景的可定制构造：允许使用非默认的注册表值名，避免污染用户现有 Run 项。
    /// </summary>
    internal StartupManager(string valueName, ILogger<StartupManager>? logger = null)
    {
        _logger = logger ?? NullLogger<StartupManager>.Instance;
        _executablePath = Environment.ProcessPath ?? Application.ExecutablePath;
        _valueName = string.IsNullOrWhiteSpace(valueName) ? DefaultValueName : valueName;
    }

    /// <summary>
    /// 检查当前是否已设置为开机自启动。
    /// </summary>
    public bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(_valueName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "检查自启动状态失败。");
            return false;
        }
    }

    /// <summary>
    /// 启用开机自启动。
    /// </summary>
    public bool EnableStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                _logger.LogError("无法打开 Run 注册表项。");
                return false;
            }

            // 使用带引号的路径
            var command = $"\"{_executablePath}\"";
            key.SetValue(_valueName, command, RegistryValueKind.String);
            _logger.LogInformation("开机自启动已启用: {Command}", command);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启用自启动失败。");
            return false;
        }
    }

    /// <summary>
    /// 禁用开机自启动。
    /// </summary>
    public bool DisableStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                _logger.LogError("无法打开 Run 注册表项。");
                return false;
            }

            key.DeleteValue(_valueName, throwOnMissingValue: false);
            _logger.LogInformation("开机自启动已禁用。");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "禁用自启动失败。");
            return false;
        }
    }

    /// <summary>
    /// 切换自启动状态。
    /// </summary>
    public bool ToggleStartup(bool enabled)
        => enabled ? EnableStartup() : DisableStartup();
}
