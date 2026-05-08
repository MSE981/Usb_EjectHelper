using Microsoft.Extensions.Logging;
using UsbEjectHelper.Core;

namespace UsbEjectHelper.UI;

/// <summary>
/// 隐藏的顶层窗口 —— 常驻接收 WM_DEVICECHANGE 广播，转发给 <see cref="DeviceWatcher"/>。
/// 与 <see cref="MainWindow"/> 解耦，主窗口关闭后仍能持续监听设备插拔。
///
/// ⚠ 必须是顶层窗口（不能挂到 HWND_MESSAGE 下），因为 Windows 对 WM_DEVICECHANGE
/// 的卷级广播（DBT_DEVICEARRIVAL/REMOVECOMPLETE + DBT_DEVTYP_VOLUME）只发给顶层窗口；
/// 消息专用窗口会被忽略。该窗口尺寸为 0、不可见、隐藏于 Alt+Tab。
/// 必须由 UI 线程创建（构造函数立即调用 <see cref="NativeWindow.CreateHandle"/>）。
/// </summary>
internal sealed class DeviceNotificationWindow : NativeWindow, IDisposable
{
    private const int WM_DEVICECHANGE = 0x0219;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private readonly DeviceWatcher _watcher;
    private readonly ILogger<DeviceNotificationWindow> _logger;
    private bool _disposed;

    public DeviceNotificationWindow(DeviceWatcher watcher, ILogger<DeviceNotificationWindow> logger)
    {
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 顶层 POPUP，0×0 大小，工具栏样式 + 不抢焦点。
        // 故意 不设置 Parent —— 这样它是顶层窗口，能收到 WM_DEVICECHANGE 广播。
        var cp = new CreateParams
        {
            Caption = "UsbEjectHelperDeviceNotify",
            X = -32000,
            Y = -32000,
            Width = 0,
            Height = 0,
            Style = WS_POPUP,
            ExStyle = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
        };
        CreateHandle(cp);
        _logger.LogInformation("DeviceNotificationWindow 已创建（顶层 HWND={Handle}）。", Handle);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DEVICECHANGE)
        {
            try
            {
                _watcher.HandleDeviceChangeMessage(m.Msg, m.WParam, m.LParam);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "处理 WM_DEVICECHANGE 时发生异常。");
            }
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            DestroyHandle();
            _logger.LogInformation("DeviceNotificationWindow 已销毁。");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "销毁 DeviceNotificationWindow 失败。");
        }
    }
}
