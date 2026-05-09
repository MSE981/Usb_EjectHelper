// UsbEjectHelper - Windows tray utility for safely ejecting USB removable storage devices.
// Copyright (C) 2026  Jin Bohan
// Licensed under GNU General Public License v3 or later. See LICENSE.

using UsbEjectHelper.Core;

namespace UsbEjectHelper.UI;

/// <summary>
/// 强制结束二次确认对话框。
/// - Normal 等级：勾选"我已了解"+ 点确认即可
/// - High   等级：必须打字精确匹配进程名才能点确认（红色横幅明示风险）
/// </summary>
public sealed class ConfirmTerminateDialog : Form
{
    private readonly Button _confirmButton;
    private readonly TextBox _typeMatchBox;
    private readonly CheckBox _acknowledgeBox;
    private readonly string _processNameForMatch;
    private readonly bool _requireTypeMatch;

    /// <summary>用户是否确认关闭。</summary>
    public bool Confirmed { get; private set; }

    /// <summary>用户同意方式（写入审计日志）。</summary>
    public string ConsentKind { get; private set; } = "declined";

    public ConfirmTerminateDialog(int pid, string processName, string exePath,
        string filePath, ProcessRiskTier tier)
    {
        _processNameForMatch = string.IsNullOrEmpty(processName) ? $"PID:{pid}" : processName;
        _requireTypeMatch = tier == ProcessRiskTier.High;

        Text = "⚠ 强制结束进程";
        Size = new Size(560, _requireTypeMatch ? 520 : 460);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var titleLabel = new Label
        {
            Text = "⚠ 强制结束进程",
            Font = new Font(SystemFonts.CaptionFont!.FontFamily, 14, FontStyle.Bold),
            ForeColor = Color.FromArgb(192, 32, 32),
            Location = new Point(20, 16),
            AutoSize = true
        };

        var infoBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Control,
            Location = new Point(20, 56),
            Size = new Size(500, 130),
            Font = SystemFonts.MessageBoxFont,
            Text = $"即将结束以下进程：\r\n\r\n"
                + $"    PID:        {pid}\r\n"
                + $"    进程名:     {processName}\r\n"
                + $"    路径:       {(string.IsNullOrEmpty(exePath) ? "[未知]" : exePath)}\r\n"
                + $"    占用文件:   {(string.IsNullOrEmpty(filePath) ? "[未知]" : filePath)}\r\n"
                + $"    风险等级:   {DescribeTier(tier)}"
        };

        var warning = new Label
        {
            Text = "⚠ 强制结束会丢失该进程未保存的数据。",
            Location = new Point(20, 200),
            Size = new Size(500, 24),
            ForeColor = Color.FromArgb(192, 32, 32),
            Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold)
        };

        _confirmButton = new Button
        {
            Text = _requireTypeMatch ? "我已了解风险，强制结束" : "强制结束",
            Location = new Point(330, _requireTypeMatch ? 440 : 380),
            Size = new Size(190, 32),
            ForeColor = Color.FromArgb(192, 32, 32),
            FlatStyle = FlatStyle.System,
            Enabled = false
        };
        _confirmButton.Click += (_, _) =>
        {
            Confirmed = true;
            ConsentKind = _requireTypeMatch ? "type-match-force" : "checkbox-force";
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancelButton = new Button
        {
            Text = "取消",
            Location = new Point(220, _requireTypeMatch ? 440 : 380),
            Size = new Size(100, 32),
            DialogResult = DialogResult.Cancel
        };
        cancelButton.Click += (_, _) =>
        {
            Confirmed = false;
            ConsentKind = "declined";
            Close();
        };
        AcceptButton = null;        // 防 Enter 直接确认
        CancelButton = cancelButton;

        Controls.AddRange(new Control[] { titleLabel, infoBox, warning, _confirmButton, cancelButton });

        if (_requireTypeMatch)
        {
            // High 等级：必须打字
            var typeHint = new Label
            {
                Text = $"请输入进程名 \"{_processNameForMatch}\" 以确认：",
                Location = new Point(20, 240),
                AutoSize = true
            };
            _typeMatchBox = new TextBox
            {
                Location = new Point(20, 270),
                Size = new Size(500, 26),
                Font = SystemFonts.MessageBoxFont
            };
            _typeMatchBox.TextChanged += (_, _) =>
            {
                _confirmButton.Enabled =
                    string.Equals(_typeMatchBox.Text.Trim(), _processNameForMatch,
                        StringComparison.OrdinalIgnoreCase);
            };
            _acknowledgeBox = new CheckBox(); // 不显示，仅占位
            Controls.AddRange(new Control[] { typeHint, _typeMatchBox });
        }
        else
        {
            // Normal 等级：勾选确认
            _acknowledgeBox = new CheckBox
            {
                Text = $"我已了解强制结束 {_processNameForMatch} 会丢失未保存的数据",
                Location = new Point(20, 250),
                AutoSize = true
            };
            _acknowledgeBox.CheckedChanged += (_, _) => _confirmButton.Enabled = _acknowledgeBox.Checked;
            _typeMatchBox = new TextBox(); // 不显示
            Controls.Add(_acknowledgeBox);
        }
    }

    private static string DescribeTier(ProcessRiskTier tier) => tier switch
    {
        ProcessRiskTier.Critical => "⚠ 系统关键（不应到达本对话框）",
        ProcessRiskTier.High =>
            "⚠ High（操作有显著副作用，例如桌面 explorer 会闪一下，杀软进程会让本机短暂失去保护）",
        _ => "Normal（普通用户进程）"
    };
}
