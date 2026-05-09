// UsbEjectHelper - Windows tray utility for safely ejecting USB removable storage devices.
// Copyright (C) 2026  Jin Bohan
// Licensed under GNU General Public License v3 or later. See LICENSE.

namespace UsbEjectHelper.Core;

/// <summary>
/// 进程关闭操作的结果。所有 IProcessTerminator 方法均不抛异常，错误通过本类型回传。
/// </summary>
public sealed record TerminationResult
{
    /// <summary>是否实际让进程退出（或确认已退出）。</summary>
    public bool Success { get; init; }

    /// <summary>
    /// 实际使用的方法或拒绝原因。固定取值集合：
    /// "Reveal" — 在资源管理器中定位
    /// "WM_CLOSE" — 优雅关闭成功
    /// "WM_CLOSE-NoWindow" — 进程没有顶层窗口，无法发 WM_CLOSE
    /// "WM_CLOSE-Timeout" — 已发 WM_CLOSE 但超时未退出
    /// "RestartManager" — 通过 RM 关闭成功
    /// "TerminateProcess" — 强制结束成功
    /// "AlreadyExited" — 进程在操作前已经退出（视为幂等成功）
    /// "Refused-Critical" — 风险等级 Critical，拒绝
    /// "Refused-NoConsent" — 设置闸门未打开，拒绝
    /// "Failed-AccessDenied" — 系统拒绝（同用户但权限不足 / EDR 拦截）
    /// "Failed-Unknown" — 其他错误，详见 Reason
    /// </summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>用户可读说明。</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>进程退出码（仅 Success=true 且能读到时填）。</summary>
    public int ExitCode { get; init; }

    /// <summary>从调用开始到结果产出的耗时。</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>关闭目标 PID（用于审计日志关联）。</summary>
    public int Pid { get; init; }

    /// <summary>关闭目标进程名（拒绝时也填，便于审计）。</summary>
    public string ProcessName { get; init; } = string.Empty;
}
