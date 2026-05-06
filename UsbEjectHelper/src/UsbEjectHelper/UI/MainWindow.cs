using Microsoft.Extensions.Logging;
using System.Text;
using UsbEjectHelper.App;
using UsbEjectHelper.Core;
using UsbEjectHelper.Settings;

namespace UsbEjectHelper.UI;

/// <summary>
/// 主窗口 —— 设备列表、操作按钮、占用结果表格、状态栏。
/// </summary>
public class MainWindow : Form
{
    private readonly TrayApplication _trayApp;
    private readonly ILogger<MainWindow> _logger;

    // 设备列表
    private readonly ListView _deviceListView;
    private readonly ColumnHeader _driveCol;
    private readonly ColumnHeader _labelCol;
    private readonly ColumnHeader _fsCol;
    private readonly ColumnHeader _capacityCol;

    // 按钮
    private readonly Button _ejectButton;
    private readonly Button _scanButton;
    private readonly Button _refreshButton;
    private readonly Button _exportButton;

    // 占用结果
    private readonly ListView _resultListView;
    private readonly ColumnHeader _pidCol;
    private readonly ColumnHeader _procNameCol;
    private readonly ColumnHeader _procPathCol;
    private readonly ColumnHeader _filePathCol;
    private readonly ColumnHeader _methodCol;

    // 状态栏
    private readonly StatusStrip _statusStrip;
    private readonly ToolStripStatusLabel _statusLabel;

    // 设备监视器
    private readonly DeviceWatcher _deviceWatcher;

    // 核心服务
    private readonly EjectService _ejectService;
    private readonly HandleScanner _handleScanner;
    private readonly VolumeResolver _volumeResolver;

    // 设置
    private readonly AppSettings _appSettings;
    private readonly StartupManager _startupManager;

    // 设置相关面板
    private readonly Panel _settingsPanel;
    private CheckBox _autoStartCheckBox = null!;
    private CheckBox _minimizeToTrayCheckBox = null!;
    private CheckBox _closeToTrayCheckBox = null!;

    /// <summary>
    /// 关闭窗口时是否最小化到托盘（true），还是退出程序（false）。
    /// </summary>
    public bool CloseToTray { get; private set; } = true;

    /// <summary>
    /// 设备刷新请求事件。
    /// </summary>
    public event EventHandler? DeviceRefreshRequested;

    public MainWindow(TrayApplication trayApp)
    {
        _trayApp = trayApp;
        _logger = LoggerFactory.Create(b => b.AddConsole().AddDebug().SetMinimumLevel(LogLevel.Information))
            .CreateLogger<MainWindow>();

        _deviceWatcher = new DeviceWatcher();
        _deviceWatcher.DevicesChanged += OnDevicesChanged;
        _deviceWatcher.Start();

        _volumeResolver = new VolumeResolver();
        _ejectService = new EjectService();
        _handleScanner = new HandleScanner(_volumeResolver, new ProcessInspector());

        // 加载设置
        _appSettings = AppSettings.Load();
        _startupManager = new StartupManager();
        CloseToTray = _appSettings.CloseToTray;

        Text = "USB Eject Helper";
        Size = new Size(800, 600);
        StartPosition = FormStartPosition.CenterScreen;

        // ---- 设备列表标签 ----
        var deviceLabel = new Label
        {
            Text = "可移动设备：",
            Location = new Point(12, 12),
            AutoSize = true
        };

        // ---- 设备列表 ----
        _deviceListView = new ListView
        {
            Location = new Point(12, 32),
            Size = new Size(760, 160),
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true
        };
        _driveCol = _deviceListView.Columns.Add("盘符", 50);
        _labelCol = _deviceListView.Columns.Add("卷标", 100);
        _fsCol = _deviceListView.Columns.Add("文件系统", 80);
        _capacityCol = _deviceListView.Columns.Add("容量", 120);

        // ---- 按钮面板 ----
        _ejectButton = new Button { Text = "弹出 (&E)", Location = new Point(12, 200), Size = new Size(90, 30) };
        _scanButton = new Button { Text = "扫描占用 (&S)", Location = new Point(110, 200), Size = new Size(90, 30) };
        _refreshButton = new Button { Text = "刷新 (&R)", Location = new Point(208, 200), Size = new Size(90, 30) };
        _exportButton = new Button { Text = "导出 JSON", Location = new Point(306, 200), Size = new Size(90, 30) };

        _ejectButton.Click += OnEject;
        _scanButton.Click += OnScan;
        _refreshButton.Click += (_, _) => DeviceRefreshRequested?.Invoke(this, EventArgs.Empty);
        _exportButton.Click += OnExport;

        // ---- 占用结果标签 ----
        var resultLabel = new Label
        {
            Text = "占用结果：",
            Location = new Point(12, 240),
            AutoSize = true
        };

        // ---- 占用结果列表 ----
        _resultListView = new ListView
        {
            Location = new Point(12, 260),
            Size = new Size(760, 200),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true
        };
        _pidCol = _resultListView.Columns.Add("PID", 60);
        _procNameCol = _resultListView.Columns.Add("进程名", 120);
        _procPathCol = _resultListView.Columns.Add("进程路径", 200);
        _filePathCol = _resultListView.Columns.Add("占用路径", 200);
        _methodCol = _resultListView.Columns.Add("检测方法", 80);

        // ---- 状态栏 ----
        _statusStrip = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel("就绪");
        _statusStrip.Items.Add(_statusLabel);

        // ---- 设置面板（初始隐藏） ----
        _settingsPanel = CreateSettingsPanel();
        _settingsPanel.Visible = false;

        // ---- 布局 ----
        Controls.AddRange(new Control[]
        {
            deviceLabel, _deviceListView,
            _ejectButton, _scanButton, _refreshButton, _exportButton,
            resultLabel, _resultListView,
            _settingsPanel,
            _statusStrip
        });

        // 窗口关闭事件
        FormClosing += OnFormClosing;

        _logger.LogInformation("主窗口已创建。");
    }

    /// <summary>
    /// 刷新设备列表（来自外部调用，需 UI 线程）。
    /// </summary>
    public void RefreshDevices()
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => RefreshDevices());
            return;
        }

        _deviceWatcher.RefreshDevices();
    }

    /// <summary>
    /// 处理 WM_DEVICECHANGE 消息，转发给 DeviceWatcher。
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        const int WM_DEVICECHANGE = 0x0219;
        if (m.Msg == WM_DEVICECHANGE)
        {
            _deviceWatcher.HandleDeviceChangeMessage(m.Msg, m.WParam, m.LParam);
        }
        base.WndProc(ref m);
    }

    /// <summary>
    /// 设备列表变化回调 —— 更新 UI 设备列表。
    /// </summary>
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

    /// <summary>
    /// 显示/隐藏设置面板。
    /// </summary>
    public void ShowSettings()
    {
        if (InvokeRequired)
        {
            BeginInvoke(ShowSettings);
            return;
        }

        _settingsPanel.Visible = !_settingsPanel.Visible;
        if (_settingsPanel.Visible)
        {
            LoadSettings();
        }
    }

    /// <summary>
    /// 设置状态栏文本。
    /// </summary>
    public void SetStatus(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(text));
            return;
        }

        _statusLabel.Text = text;
    }

    /// <summary>
    /// 填充设备列表。
    /// </summary>
    public void PopulateDevices(List<ListViewItem> items)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => PopulateDevices(items));
            return;
        }

        _deviceListView.Items.Clear();
        _deviceListView.Items.AddRange(items.ToArray());
    }

    /// <summary>
    /// 填充占用结果。
    /// </summary>
    public void PopulateScanResults(List<ListViewItem> items)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => PopulateScanResults(items));
            return;
        }

        _resultListView.Items.Clear();
        _resultListView.Items.AddRange(items.ToArray());
    }

    private Panel CreateSettingsPanel()
    {
        var panel = new Panel
        {
            Location = new Point(12, 470),
            Size = new Size(760, 80),
            BorderStyle = BorderStyle.FixedSingle
        };

        var titleLabel = new Label
        {
            Text = "设置",
            Location = new Point(8, 8),
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        };

        _autoStartCheckBox = new CheckBox
        {
            Text = "开机自启动",
            Location = new Point(8, 30),
            AutoSize = true
        };

        _minimizeToTrayCheckBox = new CheckBox
        {
            Text = "启动后最小化到托盘",
            Location = new Point(140, 30),
            AutoSize = true
        };

        _closeToTrayCheckBox = new CheckBox
        {
            Text = "关闭窗口时最小化到托盘（而非退出）",
            Location = new Point(340, 30),
            AutoSize = true,
            Checked = CloseToTray
        };
        _closeToTrayCheckBox.CheckedChanged += (_, _) =>
        {
            CloseToTray = _closeToTrayCheckBox.Checked;
        };

        var saveButton = new Button
        {
            Text = "保存设置",
            Location = new Point(640, 28),
            Size = new Size(100, 25)
        };
        saveButton.Click += OnSaveSettings;

        panel.Controls.AddRange(new Control[]
        {
            titleLabel, _autoStartCheckBox, _minimizeToTrayCheckBox, _closeToTrayCheckBox, saveButton
        });

        return panel;
    }

    private void LoadSettings()
    {
        _autoStartCheckBox.Checked = _startupManager.IsStartupEnabled();
        _minimizeToTrayCheckBox.Checked = _appSettings.MinimizeToTrayOnStart;
        _closeToTrayCheckBox.Checked = _appSettings.CloseToTray;
    }

    private void OnSaveSettings(object? sender, EventArgs e)
    {
        CloseToTray = _closeToTrayCheckBox.Checked;
        _appSettings.AutoStart = _autoStartCheckBox.Checked;
        _appSettings.MinimizeToTrayOnStart = _minimizeToTrayCheckBox.Checked;
        _appSettings.CloseToTray = _closeToTrayCheckBox.Checked;
        _appSettings.Save();

        _startupManager.ToggleStartup(_autoStartCheckBox.Checked);

        SetStatus("设置已保存。");
        _logger.LogInformation("用户设置已保存：CloseToTray={CloseToTray}, AutoStart={AutoStart}",
            CloseToTray, _autoStartCheckBox.Checked);
    }

    private async void OnEject(object? sender, EventArgs e)
    {
        if (_deviceListView.SelectedItems.Count == 0)
        {
            MessageBox.Show("请先选择一个设备。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var drive = _deviceListView.SelectedItems[0].SubItems[0].Text;
        SetStatus($"正在尝试弹出 {drive}…");
        _logger.LogInformation("弹出请求：{Drive}", drive);

        var (result, message) = await Task.Run(() => _ejectService.TryEject(drive));

        if (result == EjectResult.Success)
        {
            SetStatus(message);
            MessageBox.Show(message, "弹出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _deviceWatcher.RefreshDevices();
        }
        else if (result == EjectResult.DeviceBusy)
        {
            SetStatus($"弹出失败：{drive} 正忙。");
            var choice = MessageBox.Show(
                $"{message}\n\n是否立即扫描占用进程？",
                "弹出失败",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (choice == DialogResult.Yes)
            {
                OnScan(sender, e);
            }
        }
        else
        {
            SetStatus($"弹出失败：{message}");
            MessageBox.Show(message, "弹出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void OnScan(object? sender, EventArgs e)
    {
        if (_deviceListView.SelectedItems.Count == 0)
        {
            MessageBox.Show("请先选择一个设备。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var drive = _deviceListView.SelectedItems[0].SubItems[0].Text;
        SetStatus($"正在扫描 {drive} 的占用（Restart Manager）…");
        _logger.LogInformation("扫描请求：{Drive}", drive);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var summary = await Task.Run(() => _handleScanner.Scan(drive, cts.Token));

        _resultListView.Items.Clear();

        if (!summary.HasResults)
        {
            _resultListView.Items.Add(new ListViewItem(new[]
            {
                "--", "当前扫描方法未发现占用", summary.LimitationNote, "", "RM"
            }));
            SetStatus($"扫描完成：未发现占用。{summary.LimitationNote}");
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
            SetStatus($"扫描完成：发现 {summary.Results.Count} 个可能的占用。");
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
                // 收集当前设备信息
                var devices = _deviceListView.Items
                    .OfType<ListViewItem>()
                    .Select(item => item.Tag as DeviceInfo)
                    .Where(d => d != null)
                    .ToList();

                // 如果有扫描结果，导出扫描结果；否则导出设备列表
                string json;
                if (_resultListView.Items.Count > 0 &&
                    _resultListView.Items[0].SubItems[0].Text != "--")
                {
                    // 构建简化的 ScanSummary 用于导出
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
                    json = ExportService.ExportScanResults(summary, _appSettings.EnablePrivacyMode);
                }
                else
                {
                    json = ExportService.ExportDevices(devices!);
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
            _deviceWatcher?.Dispose();
            _handleScanner?.Dispose();
            _ejectService?.Dispose();
            _volumeResolver?.Dispose();
        }
        base.Dispose(disposing);
    }
}
