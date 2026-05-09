// UsbEjectHelper - Windows tray utility for safely ejecting USB removable storage devices.
// Copyright (C) 2026  Jin Bohan
// Licensed under GNU General Public License v3 or later. See LICENSE.

namespace UsbEjectHelper.Core;

/// <summary>
/// 强制弹出抽象 —— Lock(best-effort) → Dismount → Eject 三步。
/// dismount 完成后所有持该卷句柄的进程会得到 invalid handle，未刷写缓冲会丢失。
/// 仅对**真正的 USB removable**有效；固定盘 / 系统盘必须在 Validate 阶段拒绝。
/// </summary>
public interface IForceEjectService
{
    ForceEjectResult ForceEject(string driveLetter, CancellationToken ct = default);
}
