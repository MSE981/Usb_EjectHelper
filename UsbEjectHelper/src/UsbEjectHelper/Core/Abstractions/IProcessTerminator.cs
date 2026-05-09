// UsbEjectHelper - Windows tray utility for safely ejecting USB removable storage devices.
// Copyright (C) 2026  Jin Bohan
// Licensed under GNU General Public License v3 or later. See LICENSE.

namespace UsbEjectHelper.Core;

/// <summary>
/// 进程关闭抽象 —— 四层方法（揭示 / WM_CLOSE / Restart Manager / TerminateProcess）。
/// 所有方法不抛异常，错误通过 <see cref="TerminationResult"/> 回传，便于 UI 同步显示与审计日志记录。
/// </summary>
public interface IProcessTerminator
{
    /// <summary>L1: 在资源管理器中定位进程的可执行文件（高亮显示）。</summary>
    bool RevealInExplorer(int pid);

    /// <summary>
    /// L2: 向进程的所有顶层窗口发 WM_CLOSE，等待 timeout。
    /// 进程没有顶层窗口 → 返回 Method="WM_CLOSE-NoWindow"；
    /// 超时未退出 → Method="WM_CLOSE-Timeout"（应用可能在弹保存对话框）。
    /// </summary>
    TerminationResult TryCloseGracefully(int pid, TimeSpan timeout, CancellationToken ct = default);

    /// <summary>L3: 通过 Restart Manager 协议关闭进程。仅当应用向 RM 注册时有效。</summary>
    TerminationResult TryCloseViaRestartManager(int pid, CancellationToken ct = default);

    /// <summary>
    /// L4: TerminateProcess 强制结束。Critical 等级一律拒绝。
    /// 调用方有责任在 UI 层做二次确认（含 type-match）。
    /// </summary>
    TerminationResult ForceTerminate(int pid);

    /// <summary>批量优雅关闭，按 PID 分别返回结果。互不影响、不抛异常。</summary>
    IReadOnlyList<TerminationResult> CloseManyGracefully(
        IEnumerable<int> pids,
        TimeSpan perProcessTimeout,
        CancellationToken ct = default);
}
