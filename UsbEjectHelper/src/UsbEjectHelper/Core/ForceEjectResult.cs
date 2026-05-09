// UsbEjectHelper - Windows tray utility for safely ejecting USB removable storage devices.
// Copyright (C) 2026  Jin Bohan
// Licensed under GNU General Public License v3 or later. See LICENSE.

namespace UsbEjectHelper.Core;

/// <summary>
/// 强制弹出的结果。强制弹出走 Lock(best-effort) → Dismount → Eject 三步，本类型记录走到哪一步。
/// </summary>
public sealed record ForceEjectResult
{
    public bool Success { get; init; }

    /// <summary>
    /// 失败时记录卡在哪一步。固定取值：
    /// "Validate"     — 入参校验阶段（盘符不合法 / 是固定盘）
    /// "OpenVolume"   — CreateFile(\\.\X:) 失败
    /// "Lock"         — FSCTL_LOCK_VOLUME 失败（不一定阻断后续）
    /// "Dismount"     — FSCTL_DISMOUNT_VOLUME 失败（致命）
    /// "Eject"        — IOCTL_STORAGE_EJECT_MEDIA / CM_Request_Device_EjectW 失败
    /// "Done"         — 全流程成功
    /// </summary>
    public string Stage { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    /// <summary>失败时的 Win32 错误码（GetLastError）。</summary>
    public int Win32Error { get; init; }

    /// <summary>目标盘符，如 "E:"。</summary>
    public string DriveLetter { get; init; } = string.Empty;

    public TimeSpan Duration { get; init; }
}
