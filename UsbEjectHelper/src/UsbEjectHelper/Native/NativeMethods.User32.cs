// UsbEjectHelper - Windows tray utility for safely ejecting USB removable storage devices.
// Copyright (C) 2026  Jin Bohan
// Licensed under GNU General Public License v3 or later. See LICENSE.

using System.Runtime.InteropServices;
using System.Text;

namespace UsbEjectHelper.Core;

/// <summary>
/// user32.dll P/Invoke — 顶层窗口枚举与窗口消息（用于 WM_CLOSE 优雅关闭进程）。
/// </summary>
internal static partial class NativeMethods
{
    private const string User32 = "user32.dll";

    /// <summary>WM_CLOSE — 请求窗口关闭；GUI 应用通常会响应此消息（弹"是否保存"等）。</summary>
    public const uint WM_CLOSE = 0x0010;

    /// <summary>EnumWindows 回调签名。</summary>
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    /// <summary>枚举所有顶层窗口。lpEnumFunc 返回 false 中断枚举。</summary>
    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>查询窗口所属线程 / 进程。</summary>
    [DllImport(User32, SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>窗口是否对用户可见（WS_VISIBLE 标志）。隐藏窗口仍然能收 WM_CLOSE，但通常不该向其发送。</summary>
    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>非阻塞地把消息放进窗口的消息队列；与 SendMessage 不同，不等窗口处理完就返回。</summary>
    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    /// <summary>窗口标题长度（用于诊断 / 日志）。</summary>
    [DllImport(User32, CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    /// <summary>读取窗口标题。</summary>
    [DllImport(User32, CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
}
