// UsbEjectHelper - Windows tray utility for safely ejecting USB removable storage devices.
// Copyright (C) 2026  Jin Bohan
// Licensed under GNU General Public License v3 or later. See LICENSE.

using UsbEjectHelper.Core;
using UsbEjectHelper.Settings;

namespace UsbEjectHelper.UI;

/// <summary>
/// 关闭占用进程对话框。
/// 用户勾选要关哪些进程 + 选择关闭方式（优雅 / 强制），点开始后由调用方实际执行。
/// 不在本对话框内执行关闭，仅返回用户选择，便于 UI 层把"长时间动作"放在主窗口里展示进度。
/// </summary>
public sealed class CloseProcessesDialog : Form
{
    private readonly AppSettings _settings;
    private readonly ListView _list;
    private readonly RadioButton _gracefulRadio;
    private readonly RadioButton _forceRadio;
    private readonly NumericUpDown _timeoutBox;

    /// <summary>用户勾选的进程结果列表。</summary>
    public List<HandleScanResult> SelectedProcesses { get; } = new();

    /// <summary>用户选择的关闭方式：true=force, false=graceful。</summary>
    public bool UseForceTerminate { get; private set; }

    /// <summary>优雅关闭超时秒数。</summary>
    public int GracefulTimeoutSeconds { get; private set; }

    public CloseProcessesDialog(string driveLetter, IList<HandleScanResult> scanResults, AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        Text = $"关闭占用 {driveLetter} 的进程";
        Size = new Size(720, 540);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var titleLabel = new Label
        {
            Text = $"扫描发现以下进程持有 {driveLetter} 上的文件：",
            Location = new Point(16, 12),
            AutoSize = true
        };

        _list = new ListView
        {
            Location = new Point(16, 36),
            Size = new Size(680, 280),
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            GridLines = true
        };
        _list.Columns.Add("进程名", 160);
        _list.Columns.Add("PID", 60);
        _list.Columns.Add("占用路径", 280);
        _list.Columns.Add("风险", 100);

        foreach (var r in scanResults.DistinctBy(x => x.Pid))
        {
            var item = new ListViewItem(new[]
            {
                r.ProcessName,
                r.Pid > 0 ? r.Pid.ToString() : "--",
                r.FilePath,
                DescribeTier(r.RiskTier)
            });
            item.Tag = r;

            // Critical 直接 disable + 不勾选
            if (r.RiskTier == ProcessRiskTier.Critical)
            {
                item.ForeColor = SystemColors.GrayText;
                item.Checked = false;
                // Windows ListView 不直接支持 per-item 禁用 checkbox；
                // 用 ItemCheck 事件在用户尝试勾选时拦截
            }
            else
            {
                item.Checked = true;  // 默认勾选 Normal / High
                if (r.RiskTier == ProcessRiskTier.High)
                    item.BackColor = Color.FromArgb(255, 245, 235); // 浅红背景提示 High
            }

            _list.Items.Add(item);
        }

        _list.ItemCheck += (s, e) =>
        {
            var it = _list.Items[e.Index];
            if (it.Tag is HandleScanResult tag && tag.RiskTier == ProcessRiskTier.Critical)
            {
                // 阻止勾选关键进程
                e.NewValue = CheckState.Unchecked;
            }
        };

        var methodLabel = new Label
        {
            Text = "关闭方式：",
            Location = new Point(16, 330),
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold)
        };

        _gracefulRadio = new RadioButton
        {
            Text = "优雅关闭（WM_CLOSE） — 应用可弹\"是否保存\"对话框",
            Location = new Point(16, 354),
            AutoSize = true,
            Checked = true
        };

        _forceRadio = new RadioButton
        {
            Text = "强制结束（TerminateProcess） — 未保存数据丢失" +
                   (_settings.EnableForceTerminate ? "" : "  [需在设置开启]"),
            Location = new Point(16, 380),
            AutoSize = true,
            Enabled = _settings.EnableForceTerminate,
            ForeColor = _settings.EnableForceTerminate
                ? Color.FromArgb(192, 32, 32)
                : SystemColors.GrayText
        };

        var timeoutLabel = new Label
        {
            Text = "超时（秒）：",
            Location = new Point(16, 412),
            AutoSize = true
        };
        _timeoutBox = new NumericUpDown
        {
            Location = new Point(96, 408),
            Size = new Size(60, 26),
            Minimum = 1,
            Maximum = 30,
            Value = Math.Clamp(_settings.GracefulCloseTimeoutSeconds, 1, 30)
        };

        var startButton = new Button
        {
            Text = "开始关闭",
            Location = new Point(580, 460),
            Size = new Size(116, 32),
            DialogResult = DialogResult.OK
        };
        startButton.Click += (_, _) =>
        {
            UseForceTerminate = _forceRadio.Checked;
            GracefulTimeoutSeconds = (int)_timeoutBox.Value;
            SelectedProcesses.Clear();
            foreach (ListViewItem it in _list.Items)
            {
                if (it.Checked && it.Tag is HandleScanResult r)
                    SelectedProcesses.Add(r);
            }
            Close();
        };

        var cancelButton = new Button
        {
            Text = "取消",
            Location = new Point(470, 460),
            Size = new Size(100, 32),
            DialogResult = DialogResult.Cancel
        };
        cancelButton.Click += (_, _) => Close();
        AcceptButton = startButton;
        CancelButton = cancelButton;

        Controls.AddRange(new Control[]
        {
            titleLabel, _list,
            methodLabel, _gracefulRadio, _forceRadio,
            timeoutLabel, _timeoutBox,
            cancelButton, startButton
        });
    }

    private static string DescribeTier(ProcessRiskTier tier) => tier switch
    {
        ProcessRiskTier.Critical => "⚠ 系统关键",
        ProcessRiskTier.High => "⚠ High",
        _ => "Normal"
    };
}
