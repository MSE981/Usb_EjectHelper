using Microsoft.Extensions.Logging;
using UsbEjectHelper.Settings;

namespace UsbEjectHelper.UI;

/// <summary>
/// 设置对话框 —— 开机自启动、启动行为、关闭行为、隐私脱敏。
/// </summary>
public class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly StartupManager _startupManager;
    private readonly ILogger<SettingsForm> _logger;

    private readonly CheckBox _autoStartCheckBox;
    private readonly CheckBox _minimizeToTrayCheckBox;
    private readonly CheckBox _closeToTrayCheckBox;
    private readonly CheckBox _privacyModeCheckBox;
    private readonly Button _okButton;
    private readonly Button _cancelButton;

    /// <summary>对话框关闭后，最新的 CloseToTray 值（可由父窗口读取）。</summary>
    public bool CloseToTrayResult { get; private set; }

    public SettingsForm(AppSettings settings, StartupManager startupManager, ILogger<SettingsForm> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _startupManager = startupManager ?? throw new ArgumentNullException(nameof(startupManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Text = "设置";
        Size = new Size(440, 260);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        _autoStartCheckBox = new CheckBox
        {
            Text = "开机自启动",
            Location = new Point(20, 20),
            AutoSize = true,
            Checked = _startupManager.IsStartupEnabled()
        };

        _minimizeToTrayCheckBox = new CheckBox
        {
            Text = "启动后最小化到托盘",
            Location = new Point(20, 50),
            AutoSize = true,
            Checked = _settings.MinimizeToTrayOnStart
        };

        _closeToTrayCheckBox = new CheckBox
        {
            Text = "关闭窗口时最小化到托盘（而非退出）",
            Location = new Point(20, 80),
            AutoSize = true,
            Checked = _settings.CloseToTray
        };

        _privacyModeCheckBox = new CheckBox
        {
            Text = "导出时启用隐私脱敏（仅保留盘符与文件名）",
            Location = new Point(20, 110),
            AutoSize = true,
            Checked = _settings.EnablePrivacyMode
        };

        CloseToTrayResult = _settings.CloseToTray;

        _okButton = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Location = new Point(240, 170),
            Size = new Size(80, 28)
        };
        _okButton.Click += OnOk;

        _cancelButton = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(330, 170),
            Size = new Size(80, 28)
        };

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        Controls.AddRange(new Control[]
        {
            _autoStartCheckBox,
            _minimizeToTrayCheckBox,
            _closeToTrayCheckBox,
            _privacyModeCheckBox,
            _okButton,
            _cancelButton
        });
    }

    private void OnOk(object? sender, EventArgs e)
    {
        _settings.AutoStart = _autoStartCheckBox.Checked;
        _settings.MinimizeToTrayOnStart = _minimizeToTrayCheckBox.Checked;
        _settings.CloseToTray = _closeToTrayCheckBox.Checked;
        _settings.EnablePrivacyMode = _privacyModeCheckBox.Checked;
        _settings.Save();

        _startupManager.ToggleStartup(_autoStartCheckBox.Checked);

        CloseToTrayResult = _settings.CloseToTray;

        _logger.LogInformation(
            "设置已保存：AutoStart={AutoStart}, MinimizeToTray={MinTray}, CloseToTray={CloseTray}, Privacy={Privacy}",
            _settings.AutoStart, _settings.MinimizeToTrayOnStart,
            _settings.CloseToTray, _settings.EnablePrivacyMode);
    }
}
