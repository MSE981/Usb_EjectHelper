// UsbEjectHelper - Windows tray utility for safely ejecting USB removable storage devices.
// Copyright (C) 2026  Jin Bohan
// Licensed under GNU General Public License v3 or later. See LICENSE.

namespace UsbEjectHelper.Core;

/// <summary>
/// 动作审计日志 —— 记录所有进程关闭 / 强制弹出操作。JSON Lines 格式，便于反查。
/// 实现要保证：
///   1. 不抛异常（写日志失败也不影响主流程）
///   2. 文件大小超过阈值时自动滚动（actions.log → actions.1.log → ... → actions.N.log）
///   3. 隐私模式开启时 exe / filePath 字段已脱敏
/// </summary>
public interface IActionAuditLog
{
    void Append(AuditEntry entry);
}
