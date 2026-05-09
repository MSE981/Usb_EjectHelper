// UsbEjectHelper - Windows tray utility for safely ejecting USB removable storage devices.
// Copyright (C) 2026  Jin Bohan
// Licensed under GNU General Public License v3 or later. See LICENSE.

namespace UsbEjectHelper.Core;

/// <summary>
/// 一条审计日志记录。写到 actions.log（JSON Lines）。
/// 字段命名按 camelCase，与文件中的 JSON key 一一对应。
/// </summary>
public sealed record AuditEntry
{
    public DateTimeOffset Ts { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 动作类型。固定值：
    /// "close-graceful" — WM_CLOSE 优雅关闭
    /// "close-rm"       — Restart Manager 关闭
    /// "force-terminate" — TerminateProcess 强制结束
    /// "force-eject"    — Lock+Dismount+Eject 强制弹出
    /// "reveal"         — 在资源管理器中定位
    /// </summary>
    public string Action { get; init; } = string.Empty;

    public int? Pid { get; init; }
    public string? Name { get; init; }
    public string? Exe { get; init; }
    public string? Drive { get; init; }
    public string? FilePath { get; init; }
    public string Method { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string Reason { get; init; } = string.Empty;
    public long DurationMs { get; init; }

    /// <summary>
    /// 用户同意方式。固定值：
    /// "auto"                    — 系统决策（如 Critical 拒绝）
    /// "checkbox-graceful"       — 用户勾选了优雅关闭
    /// "checkbox-force"          — Normal 等级用户勾选确认
    /// "type-match-force"        — High 等级用户打字匹配进程名后确认
    /// "force-eject-2s-confirm"  — 用户在 2s 倒计时后点确认强制弹出
    /// "declined"                — 用户在确认对话框点取消
    /// </summary>
    public string Consent { get; init; } = string.Empty;
}
