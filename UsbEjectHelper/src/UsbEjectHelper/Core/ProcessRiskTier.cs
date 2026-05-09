// UsbEjectHelper - Windows tray utility for safely ejecting USB removable storage devices.
// Copyright (C) 2026  Jin Bohan
// Licensed under GNU General Public License v3 or later. See LICENSE.

namespace UsbEjectHelper.Core;

/// <summary>
/// 进程风险分级 —— 决定 UI 上是否允许关闭、需要怎样的二次确认。
/// </summary>
public enum ProcessRiskTier
{
    /// <summary>
    /// 系统不可缺。UI 拒绝任何关闭操作（L2/L3/L4 全部禁用）。
    /// 即使用户显式同意、即使管理员模式也不放开。
    /// </summary>
    Critical,

    /// <summary>
    /// 操作有显著副作用但不会让系统不可用。
    /// 例：explorer.exe（杀掉桌面会闪一下，Windows 自动重启）、Defender 相关、
    /// 杀软进程、备份代理、数据库守护。
    /// 强制结束时要求用户**打字精确匹配进程名**才能确认。
    /// </summary>
    High,

    /// <summary>普通用户进程。强制结束时勾选"我已了解"+ 点确认即可。</summary>
    Normal
}
