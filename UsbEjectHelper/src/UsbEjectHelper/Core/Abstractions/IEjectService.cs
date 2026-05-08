namespace UsbEjectHelper.Core;

/// <summary>
/// 安全弹出服务抽象。
/// </summary>
public interface IEjectService
{
    /// <summary>尝试弹出指定盘符的设备。</summary>
    (EjectResult Result, string Message) TryEject(string driveLetter);
}
