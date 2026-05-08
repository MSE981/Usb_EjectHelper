using Microsoft.Extensions.Logging;
using UsbEjectHelper.Core;

namespace UsbEjectHelper.UI;

/// <summary>
/// 隐藏的消息窗口 —— 常驻接收 WM_DEVICECHANGE，转发给 <see cref="DeviceWatcher"/>。
/// 与 <see cref="MainWindow"/> 解耦，主窗口关闭后仍能持续监听设备插拔。
/// 必须由 UI 线程创建（构造函数会立即调用 <see cref="NativeWindow.CreateHandle"/>）。
/// </summary>
internal sealed class DeviceNotificationWindow : NativeWindow, IDisposable
{
    private const int WM_DEVICECHANGE = 0x0219;
    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly DeviceWatcher _watcher;
    private readonly ILogger<DeviceNotificationWindow> _logger;
    private bool _disposed;

    public DeviceNotificationWindow(DeviceWatcher watcher, ILogger<DeviceNotificationWindow> logger)
    {
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var cp = new CreateParams
        {
            Caption = "UsbEjectHelperDeviceNotify",
            Style = WS_POPUP,
            ExStyle = WS_EX_TOOLWINDOW,
            Parent = HWND_MESSAGE
        };
        CreateHandle(cp);
        _logger.LogInformation("DeviceNotificationWindow 已创建（HWND={Handle}）。", Handle);
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
