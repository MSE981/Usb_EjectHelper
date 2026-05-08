namespace UsbEjectHelper.App;

/// <summary>
/// 全局常量 —— 单实例标识、IPC 命名管道、版本信息等。
/// </summary>
internal static class AppConstants
{
    /// <summary>固定应用 GUID，用于 Mutex / Pipe 命名。</summary>
    public const string AppGuid = "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}";

    /// <summary>当前用户级单实例互斥体名称。</summary>
    public const string MutexName = @"Local\UsbEjectHelper-" + AppGuid;

    /// <summary>当前用户级 IPC 命名管道名称。</summary>
    public const string PipeName = "UsbEjectHelper_Pipe_" + AppGuid;

    /// <summary>IPC 消息：请求显示主窗口。</summary>
    public const string IpcMessageShow = "SHOW";
}
