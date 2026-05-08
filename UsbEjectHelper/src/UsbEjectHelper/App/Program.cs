using Microsoft.Extensions.Logging;
using System.Text;

namespace UsbEjectHelper.App;

/// <summary>
/// 应用程序入口点 —— STA 线程模型、单实例检测、全局 ILoggerFactory、装配根、异常兜底。
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddConsole()
            .AddDebug());

        var bootstrapLogger = loggerFactory.CreateLogger("Bootstrap");

        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using var mutex = new Mutex(initiallyOwned: true, AppConstants.MutexName, out bool createdNew);

            if (!createdNew)
            {
                bootstrapLogger.LogInformation("检测到已有实例运行，尝试通知已有实例显示主窗口。");
                NotifyExistingInstance(bootstrapLogger);
                return;
            }

            bootstrapLogger.LogInformation("UsbEjectHelper 启动。");

            Application.ThreadException += (sender, e) =>
            {
                bootstrapLogger.LogError(e.Exception, "未处理的 UI 线程异常");
                MessageBox.Show(
                    $"发生未预期错误：{e.Exception.Message}\n\n详情已写入日志。",
                    "USB Eject Helper - 错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                bootstrapLogger.LogError(e.ExceptionObject as Exception, "未处理的 AppDomain 异常");
            };

            using var services = ServiceComposer.Build(loggerFactory);
            Application.Run(new TrayApplication(services));
        }
        catch (Exception ex)
        {
            bootstrapLogger.LogCritical(ex, "程序启动失败");
            MessageBox.Show(
                $"程序启动失败：{ex.Message}",
                "USB Eject Helper - 启动失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            bootstrapLogger.LogInformation("UsbEjectHelper 退出。");
        }
    }

    /// <summary>通过命名管道通知已有实例显示主窗口。</summary>
    private static void NotifyExistingInstance(ILogger logger)
    {
        try
        {
            using var client = new System.IO.Pipes.NamedPipeClientStream(
                ".", AppConstants.PipeName, System.IO.Pipes.PipeDirection.Out);
            client.Connect(timeout: 2000);
            var message = Encoding.UTF8.GetBytes(AppConstants.IpcMessageShow);
            client.Write(message, 0, message.Length);
            logger.LogInformation("已通知已有实例显示主窗口。");
        }
        catch (TimeoutException)
        {
            logger.LogWarning("通知已有实例超时，静默退出。");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "通知已有实例失败，静默退出。");
        }
    }
}
