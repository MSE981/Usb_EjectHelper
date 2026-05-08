using Microsoft.Extensions.Logging;
using System.Text;
using UsbEjectHelper.App;
using UsbEjectHelper.Core;

namespace UsbEjectHelper.UI;

/// <summary>
/// 主窗口 —— 设备列表、操作按钮、占用结果表格、状态栏。
/// 服务由 <see cref="ServiceComposer"/> 注入，本窗口不创建也不释放服务。
/// 设置由 <see cref="SettingsForm"/> 提供；WM_DEVICECHANGE 由 <see cref="DeviceNotificationWindow"/> 接收。
/// </summary>
public class MainWindow : Form
{
    private readonly TrayApplication _trayApp;
    private readonly ServiceComposer _services;
    private readonly ILogger<MainWindow> _logger;

    private readonly ListView _deviceListView;
    private readonly Button _ejectButton;
    private readonly Button _scanButton;
    private readonly Button _refreshButton;
    private readonly Button _exportButton;
    private readonly Button _settingsButton;
    private readonly Button _aboutButton;
    private readonly Button _hideButton;

    private readonly ListView _resultListView;
    private readonly StatusStrip _statusStrip;
    private readonly ToolStripStatusLabel _statusLabel;

    private readonly EventHandler<List<DeviceInfo>> _devicesChangedHandler;

    /// <summary>关闭窗口时是否最小化到托盘（true），还是退出程序（false）。</summary>
    public bool CloseToTray { get; private set; } = true;

    /// <summary>设备刷新请求事件。</summary>
    public event EventHandler? DeviceRefreshRequested;

    internal MainWindow(TrayApplication trayApp, ServiceComposer services)
    {
        _trayApp = trayApp ?? throw new ArgumentNullException(nameof(trayApp));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = services.LoggerFactory.CreateLogger<MainWindow>();

        CloseToTray = _services.Settings.CloseToTray;

        Text = "USB Eject Helper";
        Size = new Size(800, 560);
        StartPosition = FormStartPosition.CenterScreen;

        var deviceLabel = new Label
        {
            Text = "可移动设备：",
            Location = new Point(12, 12),
            AutoSize = true
        };

        _deviceListView = new ListView
        {
            Location = new Point(12, 32),
            Size = new Size(760, 160),
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true
        };
        _deviceListView.Columns.Add("盘符", 50);
        _deviceListView.Columns.Add("卷标", 100);
        _deviceListView.Columns.Add("文件系统", 80);
        _deviceListView.Columns.Add("容量", 120);

        _ejectButton    = new Button { Text = "弹出 (&E)",     Location = new Point(  12, 200), Size = new Size(90, 30) };
        _scanButton     = new Button { Text = "扫描占用 (&S)",  Location = new Point( 110, 200), Size = new Size(90, 30) };
        _refreshButton  = new Button { Text = "刷新 (&R)",      Location = new Point( 208, 200), Size = new Size(90, 30) };
        _exportButton   = new Button { Text = "导出 JSON",      Location = new Point( 306, 200), Size = new Size(90, 30) };
        _settingsButton = new Button { Text = "设置 (&O)",      Location = new Point( 404, 200), Size = new Size(90, 30) };
        _aboutButton    = new Button { Text = "关于 (&A)",      Location = new Point( 502, 200), Size = new Size(80, 30) };
        _hideButton     = new Button { Text = "隐藏到托盘 (&H)", Location = new Point( 590, 200), Size = new Size(110, 30) };

        _ejectButton.Click   += OnEject;
        _scanButton.Click    += OnScan;
        _refreshButton.Click += (_, _) => DeviceRefreshRequested?.Invoke(this, EventArgs.Empty);
        _exportButton.Click  += OnExport;
        _settingsButton.Click += (_, _) => ShowSettings();
        _aboutButton.Click   += (_, _) => ShowAbout();
        _hideButton.Click    += (_, _) => Hide();

        var resultLabel = new Label
        {
            Text = "占用结果：",
            Location = new Point(12, 240),
            AutoSize = true
        };

        _resultListView = new ListView
        {
            Location = new Point(12, 260),
            Size = new Size(760, 220),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true
        };
        _resultListView.Columns.Add("PID", 60);
        _resultListView.Columns.Add("进程名", 120);
        _resultListView.Columns.Add("进程路径", 200);
        _resultListView.Columns.Add("占用路径", 200);
        _resultListView.Columns.Add("检测方法", 80);

        _statusStrip = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel("就绪");
        _statusStrip.Items.Add(_statusLabel);

        Controls.AddRange(new Control[]
        {
            deviceLabel, _deviceListView,
            _ejectButton, _scanButton, _refreshButton, _exportButton,
            _settingsButton, _aboutButton, _hideButton,
            resultLabel, _resultListView,
            _statusStrip
        });

        FormClosing += OnFormClosing;

        _devicesChangedHandler = OnDevicesChanged;
        _services.DeviceWatcher.DevicesChanged += _devicesChangedHandler;
        OnDevicesChanged(this, _services.DeviceWatcher.Devices.ToList());

        _logger.LogInformation("主窗口已创建。");
    }

    /// <summary>刷新设备列表（来自外部调用，需 UI 线程）。</summary>
    public void RefreshDevices()
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => RefreshDevices());
            return;
        }

        _services.DeviceWatcher.RefreshDevices();
    }

    private void OnDevicesChanged(object? sender, List<DeviceInfo> devices)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnDevicesChanged(sender, devices));
            return;
        }

        _deviceListView.Items.Clear();

        if (devices.Count == 0)
        {
            _deviceListView.Items.Add(new ListViewItem(new[] { "--", "未检测到可弹出设备", "", "" }));
            SetStatus("未检测到可弹出设备。");
            return;
        }

        foreach (var device in devices)
        {
            var item = new ListViewItem(new[]
            {
                device.DriveLetter,
                device.VolumeLabel,
                device.FileSystem,
                device.CapacityDisplay
            });
            item.Tag = device;
            _deviceListView.Items.Add(item);
        }

        SetStatus($"已检测到 {devices.Count} 个可弹出设备。");
    }

    /// <summary>打开"关于"对话框（模态）。</summary>
    public void ShowAbout()
    {
        if (InvokeRequired)
        {
            BeginInvoke(ShowAbout);
            return;
        }

        using var dlg = new AboutDialog();
        dlg.ShowDialog(this);
    }

    /// <summary>打开设置对话框（模态）。</summary>
    public void ShowSettings()
    {
        if (InvokeRequired)
        {
            BeginInvoke(ShowSettings);
            return;
        }

        var logger = _services.LoggerFactory.CreateLogger<SettingsForm>();
        using var dlg = new SettingsForm(_services.Settings, _services.StartupManager, logger);
        var result = dlg.ShowDialog(this);
        if (result == DialogResult.OK)
        {
            CloseToTray = dlg.CloseToTrayResult;
            SetStatus("设置已保存。");
        }
    }

    /// <summary>设置状态栏文本。</summary>
    public void SetStatus(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(text));
            return;
        }

        _statusLabel.Text = text;
    }

    /// <summary>正在执行弹出 / 扫描的动作锁，防止 async void 处理器被重复触发导致重复对话框。</summary>
    private int _busy;

    private async void OnEject(object? sender, EventArgs e)
    {
        // CompareExchange 单次拿锁；正在弹出时直接吞掉新的点击。
        if (System.Threading.Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            _logger.LogDebug("OnEject 正在执行，忽略并发点击。");
            return;
        }

        try
        {
            if (_deviceListView.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先选择一个设备。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var drive = _deviceListView.SelectedItems[0].SubItems[0].Text;
            SetStatus($"正在尝试弹出 {drive}…");
            _logger.LogInformation("弹出请求：{Drive}", drive);

            SetActionButtonsEnabled(false);
            var (result, message) = await Task.Run(() => _services.EjectService.TryEject(drive));

            if (result == EjectResult.Success)
            {
                SetStatus(message);
                MessageBox.Show(message, "弹出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _services.DeviceWatcher.RefreshDevices();
            }
            else if (result == EjectResult.DeviceBusy || result == EjectResult.DeviceBusyVetoed)
            {
                var title = result == EjectResult.DeviceBusyVetoed ? "弹出被拒绝" : "弹出失败";
                SetStatus($"{title}：{drive} 正忙。");
                var choice = MessageBox.Show(
                    $"{message}\n\n是否立即扫描占用进程？",
                    title,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (choice == DialogResult.Yes)
                {
                    // 推迟到 OnEject 释放 _busy 之后再触发扫描，避免被自己的并发锁吞掉
                    BeginInvoke(() => OnScan(sender, e));
                }
            }
            else
            {
                SetStatus($"弹出失败：{message}");
                MessageBox.Show(message, "弹出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            SetActionButtonsEnabled(true);
            System.Threading.Interlocked.Exchange(ref _busy, 0);
        }
    }

    private void SetActionButtonsEnabled(bool enabled)
    {
        _ejectButton.Enabled = enabled;
        _scanButton.Enabled = enabled;
        _refreshButton.Enabled = enabled;
        _exportButton.Enabled = enabled;
    }

    private async void OnScan(object? sender, EventArgs e)
    {
        if (System.Threading.Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            _logger.LogDebug("OnScan 正在执行，忽略并发点击。");
            return;
        }

        try
        {
            if (_deviceListView.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先选择一个设备。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var drive = _deviceListView.SelectedItems[0].SubItems[0].Text;
            SetStatus($"正在扫描 {drive} 的占用…");
            _logger.LogInformation("扫描请求：{Drive}", drive);

            SetActionButtonsEnabled(false);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            // 安全 / 深度模式由用户在设置里显式选择；默认安全（仅 RM）。
            var allowDeep = _services.Settings.EnableDeepHandleScan;
            var summary = await Task.Run(() => _services.HandleScanner.Scan(drive, allowDeep, cts.Token));

            _resultListView.Items.Clear();

            if (!summary.HasResults)
            {
                _resultListView.Items.Add(new ListViewItem(new[]
                {
                    "--", "未发现占用", summary.LimitationNote, "", summary.Method
                }));
                SetStatus($"扫描完成：未发现占用（方法：{summary.Method}）。");
            }
            else
            {
                foreach (var r in summary.Results)
                {
                    var riskTag = r.IsCriticalProcess ? "⚠ 系统进程" : "";
                    var item = new ListViewItem(new[]
                    {
                        r.Pid > 0 ? r.Pid.ToString() : "--",
                        r.ProcessName,
                        r.ExecutablePath,
                        r.FilePath,
                        r.DetectionMethod + (string.IsNullOrEmpty(riskTag) ? "" : $" {riskTag}")
                    });
                    _resultListView.Items.Add(item);
                }
                SetStatus($"扫描完成：发现 {summary.Results.Count} 个占用进程（方法：{summary.Method}）。");
            }
        }
        finally
        {
            SetActionButtonsEnabled(true);
            System.Threading.Interlocked.Exchange(ref _busy, 0);
        }
    }

    private void OnExport(object? sender, EventArgs e)
    {
        using var sfd = new SaveFileDialog
        {
            Filter = "JSON 文件 (*.json)|*.json|文本文件 (*.txt)|*.txt",
            DefaultExt = "json",
            FileName = $"UsbEjectHelper_Export_{DateTime.Now:yyyyMMdd_HHmmss}"
        };

        if (sfd.ShowDialog() == DialogResult.OK)
        {
            try
            {
                var devices = _deviceListView.Items
                    .OfType<ListViewItem>()
                    .Select(item => item.Tag as DeviceInfo)
                    .Where(d => d != null)
                    .ToList();

                string json;
                if (_resultListView.Items.Count > 0 &&
                    _resultListView.Items[0].SubItems[0].Text != "--")
                {
                    var results = _resultListView.Items
                        .OfType<ListViewItem>()
                        .Select(item => new HandleScanResult
                        {
                            Pid = int.TryParse(item.SubItems[0].Text, out var p) ? p : 0,
                            ProcessName = item.SubItems[1].Text,
                            ExecutablePath = item.SubItems[2].Text,
                            FilePath = item.SubItems[3].Text,
                            DetectionMethod = item.SubItems[4].Text
                        }).ToList();

                    var summary = new ScanSummary
                    {
                        TargetDrive = _deviceListView.SelectedItems.Count > 0
                            ? _deviceListView.SelectedItems[0].SubItems[0].Text : "未知",
                        Results = results
                    };
                    json = _services.ExportService.ExportScanResults(summary, _services.Settings.EnablePrivacyMode);
                }
                else
                {
                    json = _services.ExportService.ExportDevices(devices!);
                }

                File.WriteAllText(sfd.FileName, json, Encoding.UTF8);
                SetStatus($"已导出到：{sfd.FileName}");
                _logger.LogInformation("导出完成：{Path}", sfd.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出失败。");
                SetStatus("导出失败。");
            }
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (CloseToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            SetStatus("程序已最小化到托盘。双击托盘图标可重新打开。");
            _logger.LogInformation("主窗口已隐藏到托盘。");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _services.DeviceWatcher.DevicesChanged -= _devicesChangedHandler;
        }
        base.Dispose(disposing);
    }
}
