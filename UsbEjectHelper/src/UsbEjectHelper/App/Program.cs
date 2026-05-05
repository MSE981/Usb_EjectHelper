using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text;

namespace UsbEjectHelper.App;

/// <summary>
/// 应用程序入口点 —— 负责 STA 线程模型、单实例检测、异常兜底日志。
/// </summary>
public static class Program
{
    private const string AppGuid = "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}";
    private const string MutexName = @"Local\UsbEjectHelper-" + AppGuid;
    private const string PipeName = "UsbEjectHelper_Pipe_" + AppGuid;

    private static readonly ILoggerFactory LoggerFactoryInstance =
        Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(LogLevel.Information)
                .AddConsole()
                .AddDebug();
        });

    private static readonly ILogger<object> Log = LoggerFactoryInstance.CreateLogger<object>();

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);

            if (!createdNew)
            {
                Log.LogInformation("检测到已有实例运行，尝试通知已有实例显示主窗口。");
                NotifyExistingInstance();
                return; // 静默退出
            }

            Log.LogInformation("UsbEjectHelper 启动。");

            // 捕获未处理异常
            Application.ThreadException += (sender, e) =>
            {
                Log.LogError(e.Exception, "未处理的 UI 线程异常");
                MessageBox.Show(
                    $"发生未预期错误：{e.Exception.Message}\n\n详情已写入日志。",
                    "USB Eject Helper - 错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Log.LogError(e.ExceptionObject as Exception, "未处理的 AppDomain 异常");
            };

            Application.Run(new TrayApplication());
        }
        catch (Exception ex)
        {
            Log.LogCritical(ex, "程序启动失败");
            MessageBox.Show(
                $"程序启动失败：{ex.Message}",
                "USB Eject Helper - 启动失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            Log.LogInformation("UsbEjectHelper 退出。");
            LoggerFactoryInstance.Dispose();
        }
    }

    /// <summary>
    /// 通过命名管道通知已有实例显示主窗口。
    /// </summary>
    private static void NotifyExistingInstance()
    {
        try
        {
            using var client = new System.IO.Pipes.NamedPipeClientStream(".", PipeName, System.IO.Pipes.PipeDirection.Out);
            client.Connect(timeout: 2000);
            var message = Encoding.UTF8.GetBytes("SHOW");
            client.Write(message, 0, message.Length);
            Log.LogInformation("已通知已有实例显示主窗口。");
        }
        catch (TimeoutException)
        {
            Log.LogWarning("通知已有实例超时，静默退出。");
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "通知已有实例失败，静默退出。");
        }
    }
}
