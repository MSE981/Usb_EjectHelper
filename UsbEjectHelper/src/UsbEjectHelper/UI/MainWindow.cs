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

        // 占用列表的右键菜单（L1 揭示 / L2 优雅关闭 / L4 强制结束 / 复制路径）
        AttachResultContextMenu();

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
                var title = result == EjectResult.DeviceBusyVetoed ? "弹出被拒绝" : "设备繁忙";
                SetStatus($"{title}：{drive} 正忙。");

                using var dlg = new EjectFailureDialog(drive, message, _services.Settings);
                dlg.ShowDialog(this);

                // 用 BeginInvoke 推迟动作执行，让 OnEject 先释放 _busy 锁
                switch (dlg.Choice)
                {
                    case EjectFailureChoice.Scan:
                        BeginInvoke(() => OnScan(sender, e));
                        break;
                    case EjectFailureChoice.CloseProcesses:
                        BeginInvoke(() => OnCloseProcessesForDrive(drive));
                        break;
                    case EjectFailureChoice.ForceEject:
                        BeginInvoke(() => OnForceEject(drive));
                        break;
                    case EjectFailureChoice.None:
                    default:
                        break;
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
                    var riskTag = r.RiskTier switch
                    {
                        ProcessRiskTier.Critical => "⚠ 系统关键",
                        ProcessRiskTier.High => "⚠ High",
                        _ => string.Empty
                    };
                    var item = new ListViewItem(new[]
                    {
                        r.Pid > 0 ? r.Pid.ToString() : "--",
                        r.ProcessName,
                        r.ExecutablePath,
                        r.FilePath,
                        r.DetectionMethod + (string.IsNullOrEmpty(riskTag) ? "" : $" {riskTag}")
                    });
                    item.Tag = r;
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

    /// <summary>
    /// 关闭占用进程流程：先扫描，再让用户在对话框里选要关哪些 + 怎么关，执行后回主窗口（不自动重试弹出）。
    /// </summary>
    private async void OnCloseProcessesForDrive(string drive)
    {
        if (System.Threading.Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            _logger.LogDebug("OnCloseProcessesForDrive 正在执行，忽略重入。");
            return;
        }

        try
        {
            SetStatus($"正在扫描 {drive} 的占用…");
            SetActionButtonsEnabled(false);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var allowDeep = _services.Settings.EnableDeepHandleScan;
            var summary = await Task.Run(() => _services.HandleScanner.Scan(drive, allowDeep, cts.Token));

            if (!summary.HasResults)
            {
                MessageBox.Show(
                    $"扫描完成但未发现占用进程。\n\n方法：{summary.Method}\n{summary.LimitationNote}",
                    "无可关闭的占用",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                SetStatus("扫描完成：未发现占用。");
                return;
            }

            using var dlg = new CloseProcessesDialog(drive, summary.Results, _services.Settings);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedProcesses.Count == 0)
            {
                SetStatus("已取消关闭进程。");
                return;
            }

            var pids = dlg.SelectedProcesses.Select(r => r.Pid).Where(p => p > 0).ToList();
            var useForce = dlg.UseForceTerminate;
            var timeout = TimeSpan.FromSeconds(dlg.GracefulTimeoutSeconds);
            var report = new System.Text.StringBuilder();
            report.AppendLine($"已尝试关闭 {pids.Count} 个进程：");
            int succ = 0, timedOut = 0, refused = 0, failed = 0;

            await Task.Run(() =>
            {
                foreach (var r in dlg.SelectedProcesses)
                {
                    if (r.Pid <= 0) continue;

                    TerminationResult tr;
                    if (useForce && r.RiskTier != ProcessRiskTier.Normal)
                    {
                        // High 等级走打字确认；这里要回到 UI 线程
                        bool confirmed = false;
                        string consent = "declined";
                        Invoke(() =>
                        {
                            using var conf = new ConfirmTerminateDialog(
                                r.Pid, r.ProcessName, r.ExecutablePath, r.FilePath, r.RiskTier);
                            conf.ShowDialog(this);
                            confirmed = conf.Confirmed;
                            consent = conf.ConsentKind;
                        });

                        if (!confirmed)
                        {
                            refused++;
                            report.AppendLine($"  ⊘ {r.ProcessName} ({r.Pid}) — 用户取消");
                            _services.ActionAuditLog.Append(new AuditEntry
                            {
                                Action = "force-terminate", Pid = r.Pid, Name = r.ProcessName,
                                Exe = r.ExecutablePath, Drive = drive, FilePath = r.FilePath,
                                Method = "Refused-NoConsent", Success = false, Reason = "用户取消",
                                Consent = consent
                            });
                            continue;
                        }

                        tr = _services.ProcessTerminator.ForceTerminate(r.Pid);
                    }
                    else if (useForce)
                    {
                        // Normal 等级也走 ConfirmTerminateDialog（勾选确认）
                        bool confirmed = false;
                        string consent = "declined";
                        Invoke(() =>
                        {
                            using var conf = new ConfirmTerminateDialog(
                                r.Pid, r.ProcessName, r.ExecutablePath, r.FilePath, r.RiskTier);
                            conf.ShowDialog(this);
                            confirmed = conf.Confirmed;
                            consent = conf.ConsentKind;
                        });

                        if (!confirmed)
                        {
                            refused++;
                            report.AppendLine($"  ⊘ {r.ProcessName} ({r.Pid}) — 用户取消");
                            _services.ActionAuditLog.Append(new AuditEntry
                            {
                                Action = "force-terminate", Pid = r.Pid, Name = r.ProcessName,
                                Exe = r.ExecutablePath, Drive = drive, FilePath = r.FilePath,
                                Method = "Refused-NoConsent", Success = false, Reason = "用户取消",
                                Consent = consent
                            });
                            continue;
                        }
                        tr = _services.ProcessTerminator.ForceTerminate(r.Pid);
                    }
                    else
                    {
                        tr = _services.ProcessTerminator.TryCloseGracefully(r.Pid, timeout);
                    }

                    // 写审计日志
                    _services.ActionAuditLog.Append(new AuditEntry
                    {
                        Action = useForce ? "force-terminate" : "close-graceful",
                        Pid = tr.Pid, Name = tr.ProcessName,
                        Exe = r.ExecutablePath, Drive = drive, FilePath = r.FilePath,
                        Method = tr.Method, Success = tr.Success, Reason = tr.Reason,
                        DurationMs = (long)tr.Duration.TotalMilliseconds,
                        Consent = useForce
                            ? (r.RiskTier == ProcessRiskTier.High ? "type-match-force" : "checkbox-force")
                            : "checkbox-graceful"
                    });

                    if (tr.Success) { succ++; report.AppendLine($"  ✓ {r.ProcessName} ({r.Pid}) — 已退出 ({tr.Method})"); }
                    else if (tr.Method.Contains("Timeout")) { timedOut++; report.AppendLine($"  ⏱ {r.ProcessName} ({r.Pid}) — 超时（应用可能弹了'是否保存'对话框）"); }
                    else { failed++; report.AppendLine($"  ✗ {r.ProcessName} ({r.Pid}) — {tr.Reason}"); }
                }
            });

            report.AppendLine();
            report.AppendLine($"成功 {succ} | 超时 {timedOut} | 用户取消 {refused} | 其他失败 {failed}");
            report.AppendLine();
            report.AppendLine("请处理仍在运行的应用（如保存对话框）后，再点\"弹出\"按钮重试。");

            MessageBox.Show(report.ToString(), "关闭进程结果",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetStatus($"关闭进程完成：成功 {succ} / 共 {pids.Count}");
        }
        finally
        {
            SetActionButtonsEnabled(true);
            System.Threading.Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>
    /// 给 _resultListView 挂上右键菜单 —— L1 揭示 / L2 优雅关闭 / L4 强制结束 / 复制路径。
    /// 菜单项的 enabled 状态根据当前选中行的 RiskTier 与设置闸门动态决定。
    /// </summary>
    private void AttachResultContextMenu()
    {
        var menu = new ContextMenuStrip();
        var revealItem = new ToolStripMenuItem("在资源管理器中定位");
        var gracefulItem = new ToolStripMenuItem("尝试优雅关闭");
        var forceItem = new ToolStripMenuItem("强制结束进程...") { ForeColor = Color.FromArgb(192, 32, 32) };
        var copyItem = new ToolStripMenuItem("复制进程路径");

        menu.Items.AddRange(new ToolStripItem[]
        {
            revealItem,
            new ToolStripSeparator(),
            gracefulItem,
            forceItem,
            new ToolStripSeparator(),
            copyItem
        });

        revealItem.Click += (_, _) =>
        {
            var r = GetSelectedScanResult();
            if (r is null || r.Pid <= 0) return;
            _services.ProcessTerminator.RevealInExplorer(r.Pid);
            _services.ActionAuditLog.Append(new AuditEntry
            {
                Action = "reveal", Pid = r.Pid, Name = r.ProcessName, Exe = r.ExecutablePath,
                FilePath = r.FilePath, Method = "Reveal", Success = true,
                Reason = "在资源管理器中定位", Consent = "auto"
            });
        };

        gracefulItem.Click += (_, _) =>
        {
            var r = GetSelectedScanResult();
            if (r is null || r.Pid <= 0) return;
            if (!_services.Settings.AllowProcessTermination)
            {
                MessageBox.Show("请先在设置中开启「允许在程序内结束占用进程」。",
                    "未启用", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            BeginInvoke(() => CloseSinglePid(r, useForce: false));
        };

        forceItem.Click += (_, _) =>
        {
            var r = GetSelectedScanResult();
            if (r is null || r.Pid <= 0) return;
            if (!_services.Settings.AllowProcessTermination || !_services.Settings.EnableForceTerminate)
            {
                MessageBox.Show("请先在设置中开启「允许在程序内结束占用进程」并启用「强制结束」选项。",
                    "未启用", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            BeginInvoke(() => CloseSinglePid(r, useForce: true));
        };

        copyItem.Click += (_, _) =>
        {
            var r = GetSelectedScanResult();
            if (r is null) return;
            try { Clipboard.SetText(r.ExecutablePath ?? string.Empty); } catch { }
        };

        // 打开菜单时根据当前选中行动态调整 enabled
        menu.Opening += (_, e) =>
        {
            var r = GetSelectedScanResult();
            if (r is null)
            {
                e.Cancel = true; // 没选中就不弹菜单
                return;
            }

            bool isCritical = r.RiskTier == ProcessRiskTier.Critical;
            bool hasPid = r.Pid > 0;
            bool hasExe = !string.IsNullOrEmpty(r.ExecutablePath) && !r.ExecutablePath.StartsWith('[');

            revealItem.Enabled = hasPid && hasExe;
            gracefulItem.Enabled = hasPid && !isCritical;
            forceItem.Enabled = hasPid && !isCritical;

            if (isCritical)
            {
                gracefulItem.ToolTipText = "系统关键进程不可关闭";
                forceItem.ToolTipText = "系统关键进程不可关闭";
            }
            else
            {
                gracefulItem.ToolTipText = string.Empty;
                forceItem.ToolTipText = string.Empty;
            }
        };

        _resultListView.ContextMenuStrip = menu;
    }

    private HandleScanResult? GetSelectedScanResult()
    {
        if (_resultListView.SelectedItems.Count == 0) return null;
        return _resultListView.SelectedItems[0].Tag as HandleScanResult;
    }

    /// <summary>
    /// 单个 PID 的关闭流程（从右键菜单触发）。force=true 走 ConfirmTerminateDialog；
    /// force=false 走 WM_CLOSE。完成后只更新状态栏，不自动 rescan。
    /// </summary>
    private async void CloseSinglePid(HandleScanResult r, bool useForce)
    {
        if (System.Threading.Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            _logger.LogDebug("CloseSinglePid 正在执行，忽略重入。");
            return;
        }

        try
        {
            SetActionButtonsEnabled(false);
            string drive = _deviceListView.SelectedItems.Count > 0
                ? _deviceListView.SelectedItems[0].SubItems[0].Text : string.Empty;

            if (useForce)
            {
                using var conf = new ConfirmTerminateDialog(
                    r.Pid, r.ProcessName, r.ExecutablePath, r.FilePath, r.RiskTier);
                conf.ShowDialog(this);
                if (!conf.Confirmed)
                {
                    SetStatus($"已取消强制结束 {r.ProcessName} ({r.Pid})。");
                    _services.ActionAuditLog.Append(new AuditEntry
                    {
                        Action = "force-terminate", Pid = r.Pid, Name = r.ProcessName,
                        Exe = r.ExecutablePath, Drive = drive, FilePath = r.FilePath,
                        Method = "Refused-NoConsent", Success = false, Reason = "用户取消",
                        Consent = conf.ConsentKind
                    });
                    return;
                }

                SetStatus($"正在强制结束 {r.ProcessName} ({r.Pid})…");
                var result = await Task.Run(() => _services.ProcessTerminator.ForceTerminate(r.Pid));
                _services.ActionAuditLog.Append(new AuditEntry
                {
                    Action = "force-terminate", Pid = result.Pid, Name = result.ProcessName,
                    Exe = r.ExecutablePath, Drive = drive, FilePath = r.FilePath,
                    Method = result.Method, Success = result.Success, Reason = result.Reason,
                    DurationMs = (long)result.Duration.TotalMilliseconds,
                    Consent = conf.ConsentKind
                });
                SetStatus(result.Success
                    ? $"强制结束成功：{result.ProcessName} ({result.Pid})"
                    : $"强制结束失败：{result.Reason}");
            }
            else
            {
                var timeout = TimeSpan.FromSeconds(_services.Settings.GracefulCloseTimeoutSeconds);
                SetStatus($"正在优雅关闭 {r.ProcessName} ({r.Pid})…");
                var result = await Task.Run(() => _services.ProcessTerminator.TryCloseGracefully(r.Pid, timeout));
                _services.ActionAuditLog.Append(new AuditEntry
                {
                    Action = "close-graceful", Pid = result.Pid, Name = result.ProcessName,
                    Exe = r.ExecutablePath, Drive = drive, FilePath = r.FilePath,
                    Method = result.Method, Success = result.Success, Reason = result.Reason,
                    DurationMs = (long)result.Duration.TotalMilliseconds,
                    Consent = "checkbox-graceful"
                });
                SetStatus(result.Success
                    ? $"优雅关闭成功：{result.ProcessName} ({result.Pid})"
                    : $"优雅关闭：{result.Method} - {result.Reason}");
            }
        }
        finally
        {
            SetActionButtonsEnabled(true);
            System.Threading.Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>
    /// 强制弹出流程：弹 2s 倒计时风险确认 → ForceEjectService → 成功/失败提示。
    /// </summary>
    private async void OnForceEject(string drive)
    {
        if (System.Threading.Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            _logger.LogDebug("OnForceEject 正在执行，忽略重入。");
            return;
        }

        try
        {
            using var dlg = new ForceEjectConfirmDialog(drive);
            dlg.ShowDialog(this);
            if (!dlg.Confirmed)
            {
                SetStatus("已取消强制弹出。");
                _services.ActionAuditLog.Append(new AuditEntry
                {
                    Action = "force-eject", Drive = drive, Method = "Refused-NoConsent",
                    Success = false, Reason = "用户在 2s 倒计时确认对话框点取消",
                    Consent = "declined"
                });
                return;
            }

            SetStatus($"正在强制弹出 {drive}…");
            SetActionButtonsEnabled(false);

            var fe = await Task.Run(() => _services.ForceEjectService.ForceEject(drive));

            _services.ActionAuditLog.Append(new AuditEntry
            {
                Action = "force-eject", Drive = drive, Method = fe.Stage,
                Success = fe.Success, Reason = fe.Reason,
                DurationMs = (long)fe.Duration.TotalMilliseconds,
                Consent = "force-eject-2s-confirm"
            });

            if (fe.Success)
            {
                SetStatus($"强制弹出成功：{drive}");
                MessageBox.Show($"已强制弹出 {drive}。\n\n{fe.Reason}",
                    "强制弹出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _services.DeviceWatcher.RefreshDevices();
            }
            else
            {
                SetStatus($"强制弹出失败：卡在 {fe.Stage}");
                MessageBox.Show($"强制弹出未能完成。\n\n阶段：{fe.Stage}\n{fe.Reason}",
                    "强制弹出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            SetActionButtonsEnabled(true);
            System.Threading.Interlocked.Exchange(ref _busy, 0);
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
