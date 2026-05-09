// UsbEjectHelper - Windows tray utility for safely ejecting USB removable storage devices.
// Copyright (C) 2026  Jin Bohan
// Licensed under GNU General Public License v3 or later. See LICENSE.

namespace UsbEjectHelper.UI;

/// <summary>
/// 强制弹出风险确认对话框。
/// 关键约束：
/// - 红色"强制弹出"按钮在 2 秒倒计时结束之前 disabled，防止误触。
/// - 不设 AcceptButton（Enter 不能直接触发），防止键盘绕过倒计时。
/// - ESC / 取消 / 关闭叉立即取消计时器并返回 Cancel。
/// </summary>
public sealed class ForceEjectConfirmDialog : Form
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly Button _confirmButton;
    private int _remainingSeconds;
    private const int TotalDelaySeconds = 2;

    /// <summary>用户是否在倒计时结束后点击了确认。</summary>
    public bool Confirmed { get; private set; }

    public ForceEjectConfirmDialog(string driveLetter)
    {
        Text = $"🔴 强制弹出 {driveLetter} — 风险确认";
        Size = new Size(560, 470);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var titleLabel = new Label
        {
            Text = $"🔴 强制弹出 {driveLetter}",
            Font = new Font(SystemFonts.CaptionFont!.FontFamily, 14, FontStyle.Bold),
            ForeColor = Color.FromArgb(192, 32, 32),
            Location = new Point(20, 16),
            AutoSize = true
        };

        var subtitle = new Label
        {
            Text = "强制弹出会：",
            Location = new Point(20, 56),
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold)
        };

        var risksBody = new Label
        {
            Text =
                "  • 跳过文件系统正常卸载流程\r\n"
              + "  • 立即让所有持有 " + driveLetter + " 上文件的应用句柄失效\r\n"
              + "  • 任何未保存到磁盘的写入将丢失\r\n"
              + "  • 拷贝中 / 写入中的文件可能损坏",
            Location = new Point(20, 84),
            Size = new Size(510, 90)
        };

        var checklistTitle = new Label
        {
            Text = "仅在以下情况使用：",
            Location = new Point(20, 184),
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold)
        };

        var checklist = new Label
        {
            Text =
                "  ✓ 你确认 U 盘上没有正在被写入的文件\r\n"
              + "  ✓ 你已经接受了潜在的数据丢失\r\n"
              + "  ✓ 你愿意为强制弹出造成的任何后果负责",
            Location = new Point(20, 210),
            Size = new Size(510, 80)
        };

        _remainingSeconds = TotalDelaySeconds;
        _confirmButton = new Button
        {
            Text = $"确认 ({_remainingSeconds})",
            Location = new Point(310, 380),
            Size = new Size(220, 36),
            Enabled = false,
            ForeColor = Color.FromArgb(192, 32, 32),
            FlatStyle = FlatStyle.System
        };
        _confirmButton.Click += (_, _) =>
        {
            Confirmed = true;
            DialogResult = DialogResult.OK;
            _timer.Stop();
            Close();
        };

        var cancelButton = new Button
        {
            Text = "取消（默认）",
            Location = new Point(180, 380),
            Size = new Size(120, 36),
            DialogResult = DialogResult.Cancel
        };
        cancelButton.Click += (_, _) =>
        {
            Confirmed = false;
            _timer.Stop();
            Close();
        };
        AcceptButton = null;     // 防止 Enter 绕过倒计时
        CancelButton = cancelButton;

        Controls.AddRange(new Control[]
        {
            titleLabel, subtitle, risksBody,
            checklistTitle, checklist,
            _confirmButton, cancelButton
        });

        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remainingSeconds--;
        if (_remainingSeconds > 0)
        {
            _confirmButton.Text = $"确认 ({_remainingSeconds})";
            return;
        }

        _timer.Stop();
        _confirmButton.Text = "强制弹出";
        _confirmButton.Enabled = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}
