// UsbEjectHelper - Windows tray utility for safely ejecting USB removable storage devices.
// Copyright (C) 2026  Jin Bohan
// Licensed under GNU General Public License v3 or later. See LICENSE.

using UsbEjectHelper.Settings;

namespace UsbEjectHelper.UI;

/// <summary>用户在弹出失败对话框上的选择。</summary>
public enum EjectFailureChoice
{
    None,
    Scan,
    CloseProcesses,
    ForceEject
}

/// <summary>
/// 弹出失败时的三选一对话框：扫描占用 / 关闭占用进程（不自动重试）/ 强制弹出。
/// 选项 ② / ③ 受 <see cref="AppSettings.AllowProcessTermination"/> /
/// <see cref="AppSettings.EnableForceEject"/> 闸门控制；未启用时灰禁用并附 tooltip。
/// </summary>
public sealed class EjectFailureDialog : Form
{
    private readonly AppSettings _settings;

    public EjectFailureChoice Choice { get; private set; } = EjectFailureChoice.None;

    public EjectFailureDialog(string driveLetter, string failureReason, AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        Text = $"无法弹出 {driveLetter}";
        Size = new Size(620, 580);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var titleLabel = new Label
        {
            Text = $"⚠ 无法弹出 {driveLetter}",
            Font = new Font(SystemFonts.CaptionFont!.FontFamily, 14, FontStyle.Bold),
            Location = new Point(20, 16),
            AutoSize = true
        };

        var reasonLabel = new Label
        {
            Text = "原因：" + failureReason,
            Location = new Point(20, 50),
            Size = new Size(560, 40),
            ForeColor = SystemColors.GrayText
        };

        var instructionLabel = new Label
        {
            Text = "请选择如何继续：",
            Location = new Point(20, 95),
            AutoSize = true
        };

        // ① 扫描
        var scanCard = BuildOptionCard(
            top: 120,
            number: "①",
            title: "扫描占用进程",
            body: "查看是哪个程序在占用，由你决定如何处理。",
            color: SystemColors.ControlDarkDark,
            enabled: true,
            tooltipWhenDisabled: null,
            onClick: () => SetChoiceAndClose(EjectFailureChoice.Scan));

        // ② 关闭进程
        bool canClose = _settings.AllowProcessTermination;
        var closeCard = BuildOptionCard(
            top: 240,
            number: "②",
            title: "关闭占用进程（之后由你手动重试弹出）",
            body: "通过 WM_CLOSE 优雅关闭。如果应用弹\"是否保存\"对话框，请先处理它，"
                + "再点\"弹出\"按钮。" + (canClose ? "" : "\n\n⚠ 需要先在设置里开启\"允许结束进程\""),
            color: canClose ? SystemColors.ControlDarkDark : SystemColors.GrayText,
            enabled: canClose,
            tooltipWhenDisabled: "在设置中开启「允许在程序内结束占用进程」后可用",
            onClick: () => SetChoiceAndClose(EjectFailureChoice.CloseProcesses));

        // ③ 强制弹出
        bool canForce = _settings.EnableForceEject;
        var forceCard = BuildOptionCard(
            top: 380,
            number: "🔴 ③",
            title: "强制弹出（有数据丢失风险）",
            body: "跳过文件系统正常卸载流程，直接卸载该卷。持有 " + driveLetter
                + " 上文件的应用会突然失去句柄，未保存的写入将丢失。"
                + (canForce ? "" : "\n\n⚠ 需要先在设置里开启\"允许强制弹出\""),
            color: canForce ? Color.FromArgb(192, 32, 32) : SystemColors.GrayText,
            enabled: canForce,
            tooltipWhenDisabled: "在设置中开启「允许强制弹出」后可用",
            onClick: () => SetChoiceAndClose(EjectFailureChoice.ForceEject));

        var closeButton = new Button
        {
            Text = "关闭",
            Location = new Point(500, 510),
            Size = new Size(90, 28),
            DialogResult = DialogResult.Cancel
        };
        closeButton.Click += (_, _) => Close();
        CancelButton = closeButton;

        Controls.AddRange(new Control[]
        {
            titleLabel, reasonLabel, instructionLabel,
            scanCard, closeCard, forceCard,
            closeButton
        });
    }

    private Panel BuildOptionCard(
        int top, string number, string title, string body, Color color,
        bool enabled, string? tooltipWhenDisabled, Action onClick)
    {
        var card = new Panel
        {
            Location = new Point(20, top),
            Size = new Size(560, 110),
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = enabled ? Cursors.Hand : Cursors.Default,
            BackColor = enabled ? SystemColors.Control : SystemColors.ControlLight
        };

        var numberLabel = new Label
        {
            Text = number,
            Location = new Point(12, 12),
            Font = new Font(SystemFonts.CaptionFont!.FontFamily, 14, FontStyle.Bold),
            ForeColor = color,
            AutoSize = true
        };
        var titleLabel = new Label
        {
            Text = title,
            Location = new Point(60, 12),
            Size = new Size(490, 22),
            Font = new Font(SystemFonts.CaptionFont!.FontFamily, 11, FontStyle.Bold),
            ForeColor = color
        };
        var bodyLabel = new Label
        {
            Text = body,
            Location = new Point(60, 36),
            Size = new Size(490, 70),
            ForeColor = enabled ? SystemColors.ControlText : SystemColors.GrayText
        };

        if (!enabled && !string.IsNullOrEmpty(tooltipWhenDisabled))
        {
            var tip = new ToolTip();
            tip.SetToolTip(card, tooltipWhenDisabled);
            tip.SetToolTip(titleLabel, tooltipWhenDisabled);
            tip.SetToolTip(bodyLabel, tooltipWhenDisabled);
        }

        if (enabled)
        {
            void Click(object? s, EventArgs e) => onClick();
            card.Click += Click;
            numberLabel.Click += Click;
            titleLabel.Click += Click;
            bodyLabel.Click += Click;
        }

        card.Controls.AddRange(new Control[] { numberLabel, titleLabel, bodyLabel });
        return card;
    }

    private void SetChoiceAndClose(EjectFailureChoice choice)
    {
        Choice = choice;
        DialogResult = DialogResult.OK;
        Close();
    }
}
