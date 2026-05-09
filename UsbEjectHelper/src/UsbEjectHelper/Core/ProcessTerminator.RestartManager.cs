// UsbEjectHelper - Windows tray utility for safely ejecting USB removable storage devices.
// Copyright (C) 2026  Jin Bohan
// Licensed under GNU General Public License v3 or later. See LICENSE.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using static UsbEjectHelper.Core.NativeMethods;

namespace UsbEjectHelper.Core;

/// <summary>
/// ProcessTerminator 的 L3 实现：用 Restart Manager 协议关闭进程。
/// 适用于明确向 RM 注册（实现 WM_QUERYENDSESSION / RM_REGISTERED_HANDLERS）的应用，例如 Office。
/// 现代多数应用并不向 RM 注册，因此 L3 经常无效；UI 层应把它列为"高级"选项。
/// </summary>
public partial class ProcessTerminator
{
    /// <inheritdoc />
    public TerminationResult TryCloseViaRestartManager(int pid, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var precheck = PrecheckForClose(pid, allowCritical: false);
        if (precheck is not null) return precheck with { Duration = sw.Elapsed };

        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException)
        {
            return BuildResult(pid, "", true, "AlreadyExited", "进程已退出", sw.Elapsed);
        }

        var name = process.ProcessName;
        process.Dispose();

        uint sessionHandle = 0;
        try
        {
            int rc = RmStartSession(out sessionHandle, 0, Guid.NewGuid().ToString());
            if (rc != ERROR_SUCCESS)
            {
                return BuildResult(pid, name, false, "Failed-Unknown",
                    $"RmStartSession 失败 (rc={rc})", sw.Elapsed);
            }

            // 注册目标进程为 RM 关闭目标
            var targetProcess = new[]
            {
                new RM_UNIQUE_PROCESS
                {
                    dwProcessId = (uint)pid,
                    ProcessStartTime = ToFileTime(GetProcessStartTime(pid))
                }
            };
            rc = RmRegisterResources(sessionHandle, 0, Array.Empty<string>(),
                (uint)targetProcess.Length, targetProcess, 0, null);
            if (rc != ERROR_SUCCESS)
            {
                return BuildResult(pid, name, false, "Failed-Unknown",
                    $"RmRegisterResources 失败 (rc={rc})", sw.Elapsed);
            }

            if (ct.IsCancellationRequested)
                return BuildResult(pid, name, false, "Failed-Unknown", "已取消", sw.Elapsed);

            // 优雅 RM shutdown：lActionFlags=0 让应用按协议自行保存退出
            rc = RmShutdown(sessionHandle, 0, IntPtr.Zero);
            if (rc != ERROR_SUCCESS)
            {
                return BuildResult(pid, name, false, "Failed-Unknown",
                    $"RmShutdown 失败 (rc={rc})；该进程很可能未向 RM 注册。", sw.Elapsed);
            }

            // 检查进程是否退出
            try
            {
                using var p = Process.GetProcessById(pid);
                p.WaitForExit(3_000);
                if (!p.HasExited)
                {
                    return BuildResult(pid, name, false, "WM_CLOSE-Timeout",
                        "RmShutdown 已发出但进程未退出（应用可能拒绝或弹了保存对话框）", sw.Elapsed);
                }
                return BuildResult(pid, name, true, "RestartManager",
                    $"进程在 {sw.ElapsedMilliseconds}ms 内退出（RM 协议）",
                    sw.Elapsed, TryGetExitCode(p));
            }
            catch (ArgumentException)
            {
                return BuildResult(pid, name, true, "RestartManager",
                    "进程已退出（RM 协议）", sw.Elapsed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TryCloseViaRestartManager 异常 PID={Pid}", pid);
            return BuildResult(pid, name, false, "Failed-Unknown", ex.Message, sw.Elapsed);
        }
        finally
        {
            if (sessionHandle != 0)
            {
                try { RmEndSession(sessionHandle); } catch { /* swallow */ }
            }
        }
    }

    private static DateTime GetProcessStartTime(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.StartTime;
        }
        catch { return DateTime.MinValue; }
    }

    private static System.Runtime.InteropServices.ComTypes.FILETIME ToFileTime(DateTime dt)
    {
        if (dt == DateTime.MinValue)
            return new System.Runtime.InteropServices.ComTypes.FILETIME();

        long ft = dt.ToFileTime();
        return new System.Runtime.InteropServices.ComTypes.FILETIME
        {
            dwLowDateTime = (int)(ft & 0xFFFFFFFFL),
            dwHighDateTime = (int)((ft >> 32) & 0xFFFFFFFFL)
        };
    }
}
