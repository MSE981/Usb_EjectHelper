// UsbEjectHelper - Windows tray utility for safely ejecting USB removable storage devices.
// Copyright (C) 2026  Jin Bohan
// Licensed under GNU General Public License v3 or later. See LICENSE.

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UsbEjectHelper.Settings;

namespace UsbEjectHelper.Core;

/// <summary>
/// 动作审计日志 —— 写到 %LOCALAPPDATA%\UsbEjectHelper\actions.log（JSON Lines）。
/// 单文件超过 maxSizeBytes 滚动，最多保留 maxFiles 份。
/// 写日志失败永远不抛，避免日志故障影响主流程。
/// </summary>
public class ActionAuditLog : IActionAuditLog
{
    private readonly object _writeLock = new();
    private readonly AppSettings _settings;
    private readonly ILogger<ActionAuditLog> _logger;
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string DefaultLogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UsbEjectHelper",
            "actions.log");

    private string LogPath => _logPathOverride ?? DefaultLogPath;
    private string? _logPathOverride;

    public ActionAuditLog(AppSettings settings, ILogger<ActionAuditLog>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? NullLogger<ActionAuditLog>.Instance;
    }

    /// <summary>仅供测试：覆盖日志文件位置。传 null 恢复默认。</summary>
    internal void OverrideLogPath(string? path) => _logPathOverride = path;

    /// <inheritdoc />
    public void Append(AuditEntry entry)
    {
        if (!_settings.EnableActionAuditLog) return;

        try
        {
            var sanitized = _settings.EnablePrivacyMode ? Sanitize(entry) : entry;

            lock (_writeLock)
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                RollIfNeeded();

                var line = JsonSerializer.Serialize(sanitized, _json) + "\n";
                File.AppendAllText(LogPath, line, Utf8NoBom);
            }
        }
        catch (Exception ex)
        {
            // 日志写入失败不影响主流程
            _logger.LogDebug(ex, "审计日志写入失败。");
        }
    }

    /// <summary>
    /// 隐私模式：脱敏路径与命令行类字段。
    /// </summary>
    private static AuditEntry Sanitize(AuditEntry e)
    {
        return e with
        {
            Exe = MaskPath(e.Exe),
            FilePath = MaskPath(e.FilePath)
        };
    }

    private static string? MaskPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        // 保留盘符与扩展名信息，中间用 *** 掩盖
        try
        {
            var dr = Path.GetPathRoot(path) ?? string.Empty;
            var ext = Path.GetExtension(path);
            return $"{dr}***{ext}";
        }
        catch { return "***"; }
    }

    private void RollIfNeeded()
    {
        try
        {
            var fi = new FileInfo(LogPath);
            if (!fi.Exists) return;

            long maxSize = Math.Max(_settings.AuditLogMaxSizeMB, 1) * 1024L * 1024L;
            if (fi.Length < maxSize) return;

            int maxFiles = Math.Max(_settings.AuditLogMaxFiles, 1);

            // 删除最老的（actions.log.{maxFiles-1}）
            string oldestPath = LogPath + "." + (maxFiles - 1);
            if (File.Exists(oldestPath))
            {
                try { File.Delete(oldestPath); } catch { }
            }

            // actions.log.N → actions.log.(N+1)，由大到小推
            for (int i = maxFiles - 2; i >= 1; i--)
            {
                var src = LogPath + "." + i;
                var dst = LogPath + "." + (i + 1);
                if (File.Exists(src))
                {
                    try { File.Move(src, dst, overwrite: true); } catch { }
                }
            }

            // actions.log → actions.log.1
            var first = LogPath + ".1";
            try { File.Move(LogPath, first, overwrite: true); } catch { }
        }
        catch
        {
            // 滚动失败也不阻塞日志写入；下一次再试
        }
    }
}
