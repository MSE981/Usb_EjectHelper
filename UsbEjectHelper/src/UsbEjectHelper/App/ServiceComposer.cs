using Microsoft.Extensions.Logging;
using UsbEjectHelper.Core;
using UsbEjectHelper.Settings;

namespace UsbEjectHelper.App;

/// <summary>
/// 应用程序装配根 —— 集中创建所有共享服务（设备监视、卷解析、弹出、扫描、设置）。
/// 单一 ILoggerFactory 由调用方传入，整个进程仅一份；服务的所有权与生命周期都集中在此。
/// </summary>
internal sealed class ServiceComposer : IDisposable
{
    public ILoggerFactory LoggerFactory { get; }
    public IWmiQueryService WmiService { get; }
    public DeviceWatcher DeviceWatcher { get; }
    public VolumeResolver VolumeResolver { get; }
    public EjectService EjectService { get; }
    public ProcessInspector ProcessInspector { get; }
    public HandleScanner HandleScanner { get; }
    public AppSettings Settings { get; }
    public StartupManager StartupManager { get; }

    private bool _disposed;

    private ServiceComposer(
        ILoggerFactory loggerFactory,
        IWmiQueryService wmiService,
        DeviceWatcher deviceWatcher,
        VolumeResolver volumeResolver,
        EjectService ejectService,
        ProcessInspector processInspector,
        HandleScanner handleScanner,
        AppSettings settings,
        StartupManager startupManager)
    {
        LoggerFactory = loggerFactory;
        WmiService = wmiService;
        DeviceWatcher = deviceWatcher;
        VolumeResolver = volumeResolver;
        EjectService = ejectService;
        ProcessInspector = processInspector;
        HandleScanner = handleScanner;
        Settings = settings;
        StartupManager = startupManager;
    }

    /// <summary>
    /// 构建装配根：按依赖顺序创建服务，并加载已持久化的设置。
    /// 不在此处启动 DeviceWatcher，由调用方决定启动时机（需要在 UI 线程消息循环建立后）。
    /// </summary>
    public static ServiceComposer Build(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var wmi = new WmiQueryService(loggerFactory.CreateLogger<WmiQueryService>());
        var deviceWatcher = new DeviceWatcher(wmi, loggerFactory.CreateLogger<DeviceWatcher>());
        var volumeResolver = new VolumeResolver(loggerFactory.CreateLogger<VolumeResolver>());
        var ejectService = new EjectService(loggerFactory.CreateLogger<EjectService>());
        var processInspector = new ProcessInspector(loggerFactory.CreateLogger<ProcessInspector>());
        var handleScanner = new HandleScanner(
            volumeResolver,
            processInspector,
            loggerFactory.CreateLogger<HandleScanner>());
        var settings = AppSettings.Load();
        var startupManager = new StartupManager(loggerFactory.CreateLogger<StartupManager>());

        return new ServiceComposer(
            loggerFactory,
            wmi,
            deviceWatcher,
            volumeResolver,
            ejectService,
            processInspector,
            handleScanner,
            settings,
            startupManager);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 按创建逆序释放（DeviceWatcher 持有 WmiService 引用，先释放消费者）
        DeviceWatcher.Dispose();
        HandleScanner.Dispose();
        EjectService.Dispose();
        ProcessInspector.Dispose();
        VolumeResolver.Dispose();
        if (WmiService is IDisposable wmiDisposable) wmiDisposable.Dispose();
    }
}
