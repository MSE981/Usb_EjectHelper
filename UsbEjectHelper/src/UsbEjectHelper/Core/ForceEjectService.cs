// UsbEjectHelper - Windows tray utility for safely ejecting USB removable storage devices.
// Copyright (C) 2026  Jin Bohan
// Licensed under GNU General Public License v3 or later. See LICENSE.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static UsbEjectHelper.Core.NativeMethods;

namespace UsbEjectHelper.Core;

/// <summary>
/// 强制弹出实现 —— Lock(best-effort) → Dismount → Eject 三步。
/// dismount 完成后所有持该卷句柄的进程会得到 invalid handle，未刷写缓冲会丢失。
/// </summary>
public class ForceEjectService : IForceEjectService
{
    private readonly ILogger<ForceEjectService> _logger;

    public ForceEjectService(ILogger<ForceEjectService>? logger = null)
    {
        _logger = logger ?? NullLogger<ForceEjectService>.Instance;
    }

    /// <inheritdoc />
    public ForceEjectResult ForceEject(string driveLetter, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // 1. Validate
        var normalized = NormalizeDriveLetter(driveLetter);
        if (normalized is null)
        {
            return new ForceEjectResult
            {
                Success = false,
                Stage = "Validate",
                Reason = $"无效的盘符：'{driveLetter}'",
                DriveLetter = driveLetter,
                Duration = sw.Elapsed
            };
        }

        if (!IsRemovableSafe(normalized))
        {
            return new ForceEjectResult
            {
                Success = false,
                Stage = "Validate",
                Reason = $"{normalized} 不是可移动设备；为防止系统损坏，强制弹出仅对可移动盘生效。",
                DriveLetter = normalized,
                Duration = sw.Elapsed
            };
        }

        // 路径用 \\.\X: 形式打开卷
        var volumePath = @"\\.\" + normalized;

        // 2. Open
        using var handle = CreateFile(
            volumePath,
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            return new ForceEjectResult
            {
                Success = false,
                Stage = "OpenVolume",
                Reason = $"无法打开卷 {volumePath}（Win32={err}: {new Win32Exception(err).Message}）",
                Win32Error = err,
                DriveLetter = normalized,
                Duration = sw.Elapsed
            };
        }

        // 3. Lock (best-effort — 失败不阻断)
        bool locked = DeviceIoControlSimple(handle, FSCTL_LOCK_VOLUME,
            IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
        if (!locked)
        {
            _logger.LogInformation("FSCTL_LOCK_VOLUME 失败 (drive={Drive}, win32={Err}) — 继续强制 dismount。",
                normalized, Marshal.GetLastWin32Error());
        }

        if (ct.IsCancellationRequested)
            return new ForceEjectResult
            {
                Success = false,
                Stage = "Lock",
                Reason = "已取消",
                DriveLetter = normalized,
                Duration = sw.Elapsed
            };

        // 4. Dismount (致命：失败则报错退出)
        bool dismounted = DeviceIoControlSimple(handle, FSCTL_DISMOUNT_VOLUME,
            IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
        if (!dismounted)
        {
            int err = Marshal.GetLastWin32Error();
            _logger.LogWarning("FSCTL_DISMOUNT_VOLUME 失败 drive={Drive}, win32={Err}", normalized, err);
            return new ForceEjectResult
            {
                Success = false,
                Stage = "Dismount",
                Reason = $"卸载文件系统失败（Win32={err}: {new Win32Exception(err).Message}）",
                Win32Error = err,
                DriveLetter = normalized,
                Duration = sw.Elapsed
            };
        }

        // 5. Eject media — 给设备发弹出命令
        bool ejected = DeviceIoControlSimple(handle, IOCTL_STORAGE_EJECT_MEDIA,
            IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
        if (!ejected)
        {
            int err = Marshal.GetLastWin32Error();
            _logger.LogInformation(
                "IOCTL_STORAGE_EJECT_MEDIA 失败 drive={Drive}, win32={Err} —— 但 dismount 已完成，卷已不可访问。",
                normalized, err);
            // dismount 已成功 = 句柄已 invalidate = 数据安全已经牺牲掉
            // 如果 IOCTL_STORAGE_EJECT_MEDIA 失败，至少卷还在系统里没被物理弹出（用户可拔但不优雅）
            return new ForceEjectResult
            {
                Success = false,
                Stage = "Eject",
                Reason = $"已卸载卷但发送弹出命令失败（Win32={err}）。可手动拔出 U 盘。",
                Win32Error = err,
                DriveLetter = normalized,
                Duration = sw.Elapsed
            };
        }

        return new ForceEjectResult
        {
            Success = true,
            Stage = "Done",
            Reason = $"强制弹出成功（lock={locked}, 用时 {sw.ElapsedMilliseconds}ms）",
            DriveLetter = normalized,
            Duration = sw.Elapsed
        };
    }

    /// <summary>
    /// 把 "e" / "E" / "E:" / "E:\" / "e:/" 统一成 "E:"。失败返回 null。
    /// </summary>
    private static string? NormalizeDriveLetter(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim().TrimEnd('\\', '/');
        if (s.Length == 1 && char.IsLetter(s[0])) s += ":";
        if (s.Length != 2 || !char.IsLetter(s[0]) || s[1] != ':') return null;
        return char.ToUpperInvariant(s[0]) + ":";
    }

    /// <summary>
    /// 严防误伤系统盘 / 固定盘。仅 DriveType=Removable 才放行。
    /// 固定 SSD / HDD / 网络盘 / CD 一律拒绝。
    /// </summary>
    private static bool IsRemovableSafe(string normalizedDriveLetter)
    {
        try
        {
            var di = new DriveInfo(normalizedDriveLetter);
            return di.DriveType == DriveType.Removable;
        }
        catch { return false; }
    }
}
