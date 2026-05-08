using Microsoft.Extensions.Logging;
using System.IO.Pipes;
using System.Text;
using UsbEjectHelper.UI;

namespace UsbEjectHelper.App;

/// <summary>
/// 托盘生命周期管理 —— 托盘图标、右键菜单、IPC 监听、应用上下文。
/// 服务由 <see cref="ServiceComposer"/> 注入，不在此类内部 new。
/// </summary>
public class TrayApplication : ApplicationContext
{
    private readonly ServiceComposer _services;
    private readonly ILogger<TrayApplication> _logger;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _showMenuItem;
    private readonly ToolStripMenuItem _refreshMenuItem;
    private readonly ToolStripMenuItem _settingsMenuItem;
    private readonly ToolStripMenuItem _exitMenuItem;
    private readonly DeviceNotificationWindow _deviceNotifyWindow;
    private MainWindow? _mainWindow;
    private CancellationTokenSource? _pipeCts;
    private bool _isExiting;

    internal TrayApplication(ServiceComposer services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = services.LoggerFactory.CreateLogger<TrayApplication>();

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "USB Eject Helper",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };

        _showMenuItem = new ToolStripMenuItem("显示主窗口 (&S)");
        _showMenuItem.Click += OnShowWindow;

        _refreshMenuItem = new ToolStripMenuItem("刷新设备列表 (&R)");
        _refreshMenuItem.Click += OnRefreshDevices;

        _settingsMenuItem = new ToolStripMenuItem("设置 (&O)");
        _settingsMenuItem.Click += OnOpenSettings;

        _exitMenuItem = new ToolStripMenuItem("退出 (&X)");
        _exitMenuItem.Click += OnExit;

        _notifyIcon.ContextMenuStrip.Items.Add(_showMenuItem);
        _notifyIcon.ContextMenuStrip.Items.Add(_refreshMenuItem);
        _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add(_settingsMenuItem);
        _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add(_exitMenuItem);

        _notifyIcon.DoubleClick += OnShowWindow;

        _logger.LogInformation("托盘已初始化。");

        _deviceNotifyWindow = new DeviceNotificationWindow(
            _services.DeviceWatcher,
            _services.LoggerFactory.CreateLogger<DeviceNotificationWindow>());

        StartIpcListener();
        _services.DeviceWatcher.Start();

        if (!_services.Settings.MinimizeToTrayOnStart)
        {
            ShowMainWindow();
        }
        else
        {
            _logger.LogInformation("启动后最小化到托盘（MinimizeToTrayOnStart=true）。");
        }
    }

    /// <summary>显示主窗口，若已存在则前置。</summary>
    public void ShowMainWindow()
    {
        if (_mainWindow == null || _mainWindow.IsDisposed)
        {
            _mainWindow = new MainWindow(this, _services);
            _mainWindow.FormClosed += (_, _) =>
            {
                if (_mainWindow?.CloseToTray == false)
                {
                    ExitApplication();
                }
                _mainWindow = null;
            };
            _mainWindow.DeviceRefreshRequested += (_, _) => OnRefreshDevices(this, EventArgs.Empty);
        }

        _mainWindow.Show();
        _mainWindow.Activate();
        if (_mainWindow.WindowState == FormWindowState.Minimized)
        {
            _mainWindow.WindowState = FormWindowState.Normal;
        }

        _logger.LogInformation("主窗口已显示。");
    }

    /// <summary>退出应用程序。</summary>
    public void ExitApplication()
    {
        if (_isExiting) return;
        _isExiting = true;

        _logger.LogInformation("正在退出…");
        _pipeCts?.Cancel();
        _pipeCts?.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _deviceNotifyWindow.Dispose();
        _mainWindow?.Close();
        _mainWindow?.Dispose();
        _mainWindow = null;
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pipeCts?.Cancel();
            _pipeCts?.Dispose();
            _notifyIcon.Dispose();
            _deviceNotifyWindow.Dispose();
            _mainWindow?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnShowWindow(object? sender, EventArgs e) => ShowMainWindow();

    private void OnRefreshDevices(object? sender, EventArgs e)
    {
        _logger.LogInformation("设备刷新已请求。");
        _services.DeviceWatcher.RefreshDevices();
    }

    private void OnOpenSettings(object? sender, EventArgs e)
    {
        _logger.LogInformation("设置窗口已请求。");
        ShowMainWindow();
        _mainWindow?.ShowSettings();
    }

    private void OnExit(object? sender, EventArgs e) => ExitApplication();

    /// <summary>启动命名管道服务端，监听来自第二个实例的通知。</summary>
    private void StartIpcListener()
    {
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        AppConstants.PipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);
                    var buffer = new byte[16];
                    await server.ReadAsync(buffer, token);
                    var msg = Encoding.UTF8.GetString(buffer).Trim('\0');

                    if (msg == AppConstants.IpcMessageShow)
                    {
                        BeginInvokeOnUiThread(ShowMainWindow);
                        _logger.LogInformation("通过 IPC 收到显示主窗口请求。");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "IPC 监听发生错误，稍后重试。");
                    try { await Task.Delay(2000, token); } catch { break; }
                }
            }
        }, token);
    }

    private void BeginInvokeOnUiThread(Action action)
    {
        var syncContext = SynchronizationContext.Current;
        if (syncContext != null)
        {
            syncContext.Post(_ => action(), null);
        }
        else if (_mainWindow is { IsDisposed: false })
        {
            _mainWindow.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }
}
