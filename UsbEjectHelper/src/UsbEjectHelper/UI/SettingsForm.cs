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
    private readonly CheckBox _deepScanCheckBox;
    private readonly Label _deepScanWarning;
    private readonly CheckBox _allowProcessTerminationCheckBox;
    private readonly CheckBox _enableForceTerminateCheckBox;
    private readonly CheckBox _enableForceEjectCheckBox;
    private readonly NumericUpDown _gracefulTimeoutBox;
    private readonly CheckBox _enableActionAuditLogCheckBox;
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
        Size = new Size(560, 660);
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

        // 深度扫描分组（高级 / 安全敏感）
        var deepScanGroupTitle = new Label
        {
            Text = "—— 高级（涉及系统级 API）——",
            Location = new Point(20, 150),
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        };
        _deepScanCheckBox = new CheckBox
        {
            Text = "启用深度占用扫描（NT 系统句柄枚举）",
            Location = new Point(20, 175),
            AutoSize = true,
            Checked = _settings.EnableDeepHandleScan
        };
        _deepScanWarning = new Label
        {
            Text =
                "默认安全模式仅用 Restart Manager，可能漏检记事本 / 图片查看器持有的文件。\n" +
                "开启后会枚举系统全量句柄表并对同用户进程 DuplicateHandle（与 Process Explorer 同路径），\n" +
                "权限上不会越过当前用户身份，但可能被部分 EDR / 杀软按启发式规则标记。",
            Location = new Point(40, 200),
            Size = new Size(490, 70),
            ForeColor = SystemColors.GrayText
        };

        // 阶段 2：进程关闭 / 强制弹出（高级 / 数据风险）
        var terminationGroupTitle = new Label
        {
            Text = "—— 高级（涉及结束进程 / 强制弹出 / 有数据风险）——",
            Location = new Point(20, 285),
            AutoSize = true,
            ForeColor = Color.FromArgb(192, 32, 32)
        };
        _allowProcessTerminationCheckBox = new CheckBox
        {
            Text = "允许在程序内结束占用进程（弹出失败时启用方式 ②）",
            Location = new Point(20, 310),
            AutoSize = true,
            Checked = _settings.AllowProcessTermination
        };
        _enableForceTerminateCheckBox = new CheckBox
        {
            Text = "启用\"强制结束\"选项（TerminateProcess） — 未保存数据会丢失",
            Location = new Point(40, 335),
            AutoSize = true,
            Checked = _settings.EnableForceTerminate,
            ForeColor = Color.FromArgb(192, 32, 32)
        };
        _enableForceEjectCheckBox = new CheckBox
        {
            Text = "允许\"强制弹出\"（FSCTL_DISMOUNT_VOLUME） — 立即让占用句柄失效",
            Location = new Point(20, 360),
            AutoSize = true,
            Checked = _settings.EnableForceEject,
            ForeColor = Color.FromArgb(192, 32, 32)
        };

        var gracefulTimeoutLabel = new Label
        {
            Text = "优雅关闭超时（秒）：",
            Location = new Point(20, 395),
            AutoSize = true
        };
        _gracefulTimeoutBox = new NumericUpDown
        {
            Location = new Point(150, 391),
            Size = new Size(60, 26),
            Minimum = 1,
            Maximum = 30,
            Value = Math.Clamp(_settings.GracefulCloseTimeoutSeconds, 1, 30)
        };

        _enableActionAuditLogCheckBox = new CheckBox
        {
            Text = "启用动作审计日志 (%LOCALAPPDATA%\\UsbEjectHelper\\actions.log)",
            Location = new Point(20, 425),
            AutoSize = true,
            Checked = _settings.EnableActionAuditLog
        };

        CloseToTrayResult = _settings.CloseToTray;

        _okButton = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Location = new Point(350, 565),
            Size = new Size(80, 28)
        };
        _okButton.Click += OnOk;

        _cancelButton = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(440, 565),
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
            deepScanGroupTitle,
            _deepScanCheckBox,
            _deepScanWarning,
            terminationGroupTitle,
            _allowProcessTerminationCheckBox,
            _enableForceTerminateCheckBox,
            _enableForceEjectCheckBox,
            gracefulTimeoutLabel,
            _gracefulTimeoutBox,
            _enableActionAuditLogCheckBox,
            _okButton,
            _cancelButton
        });
    }

    private void OnOk(object? sender, EventArgs e)
    {
        // 深度扫描首次开启时强制二次确认，避免误勾
        if (_deepScanCheckBox.Checked && !_settings.EnableDeepHandleScan)
        {
            var resp = MessageBox.Show(
                this,
                "深度扫描会做以下操作：\n" +
                "  • 调用 NtQuerySystemInformation 读取全系统句柄表（含其他用户 / SYSTEM 句柄元数据）\n" +
                "  • 对同用户进程做 DuplicateHandle 以解析占用路径\n" +
                "  • 跨用户 / 跨完整性的句柄会被 OS 自动拒绝，本程序不会绕过\n\n" +
                "这是 Process Explorer / handle.exe 同样的实现路径，不属于提权操作，\n" +
                "但仍可能被部分 EDR / 杀软按启发式规则标记。是否继续？",
                "确认开启深度扫描",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (resp != DialogResult.Yes)
            {
                _deepScanCheckBox.Checked = false;
                DialogResult = DialogResult.None; // 阻止对话框关闭
                return;
            }
        }

        // AllowProcessTermination 首次开启 → 二次确认
        if (_allowProcessTerminationCheckBox.Checked && !_settings.AllowProcessTermination)
        {
            var resp = MessageBox.Show(
                this,
                "「允许在程序内结束占用进程」开启后会启用以下能力：\n\n" +
                "  • 弹出失败对话框出现\"② 关闭占用进程\"选项\n" +
                "  • 通过 WM_CLOSE 或 TerminateProcess 关闭进程\n" +
                "  • 系统关键进程 (System / csrss / lsass 等) 永远拒绝\n" +
                "  • High 等级进程 (explorer / Defender 等) 强制结束需打字确认\n\n" +
                "操作受 Windows 用户权限约束（不会跨用户 / 不会越过 ACL）。\n" +
                "未保存数据**可能丢失**，请确认你了解风险。是否继续？",
                "确认开启进程关闭功能",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (resp != DialogResult.Yes)
            {
                _allowProcessTerminationCheckBox.Checked = false;
                DialogResult = DialogResult.None;
                return;
            }
        }

        // EnableForceEject 首次开启 → 二次确认
        if (_enableForceEjectCheckBox.Checked && !_settings.EnableForceEject)
        {
            var resp = MessageBox.Show(
                this,
                "「允许强制弹出」开启后会启用以下能力：\n\n" +
                "  • 弹出失败对话框出现\"🔴 ③ 强制弹出\"选项\n" +
                "  • 通过 FSCTL_DISMOUNT_VOLUME 直接卸载文件系统\n" +
                "  • 持有该卷文件的应用句柄会立即失效\n" +
                "  • 未保存到磁盘的写入将丢失，文件可能损坏\n\n" +
                "强制弹出仅对 USB 可移动盘有效，对系统盘 / 固定盘会被自动拒绝。\n" +
                "每次执行还需要在 2 秒倒计时确认对话框上明确确认。\n\n" +
                "是否继续？",
                "确认开启强制弹出",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (resp != DialogResult.Yes)
            {
                _enableForceEjectCheckBox.Checked = false;
                DialogResult = DialogResult.None;
                return;
            }
        }

        _settings.AutoStart = _autoStartCheckBox.Checked;
        _settings.MinimizeToTrayOnStart = _minimizeToTrayCheckBox.Checked;
        _settings.CloseToTray = _closeToTrayCheckBox.Checked;
        _settings.EnablePrivacyMode = _privacyModeCheckBox.Checked;
        _settings.EnableDeepHandleScan = _deepScanCheckBox.Checked;
        _settings.AllowProcessTermination = _allowProcessTerminationCheckBox.Checked;
        _settings.EnableForceTerminate = _enableForceTerminateCheckBox.Checked;
        _settings.EnableForceEject = _enableForceEjectCheckBox.Checked;
        _settings.GracefulCloseTimeoutSeconds = (int)_gracefulTimeoutBox.Value;
        _settings.EnableActionAuditLog = _enableActionAuditLogCheckBox.Checked;
        _settings.Save();

        _startupManager.ToggleStartup(_autoStartCheckBox.Checked);

        CloseToTrayResult = _settings.CloseToTray;

        _logger.LogInformation(
            "设置已保存：AutoStart={AutoStart}, MinimizeToTray={MinTray}, CloseToTray={CloseTray}, Privacy={Privacy}, " +
            "DeepScan={Deep}, AllowTerm={AllowTerm}, ForceTerm={ForceTerm}, ForceEject={ForceEject}, Audit={Audit}",
            _settings.AutoStart, _settings.MinimizeToTrayOnStart,
            _settings.CloseToTray, _settings.EnablePrivacyMode, _settings.EnableDeepHandleScan,
            _settings.AllowProcessTermination, _settings.EnableForceTerminate, _settings.EnableForceEject,
            _settings.EnableActionAuditLog);
    }
}
