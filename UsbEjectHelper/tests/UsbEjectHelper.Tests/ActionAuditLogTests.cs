using System.Text.Json;
using UsbEjectHelper.Core;
using UsbEjectHelper.Settings;
using Xunit;

namespace UsbEjectHelper.Tests;

/// <summary>
/// ActionAuditLog 测试。
/// 用临时文件验证写入 / JSON 解析 / 滚动 / 隐私脱敏。
/// </summary>
public class ActionAuditLogTests : IDisposable
{
    private readonly string _tempFile;

    public ActionAuditLogTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"UsbEjectHelper_Audit_{Guid.NewGuid():N}.log");
    }

    public void Dispose()
    {
        SafeDelete(_tempFile);
        for (int i = 1; i < 10; i++) SafeDelete(_tempFile + "." + i);
    }

    private static void SafeDelete(string p)
    {
        try { if (File.Exists(p)) File.Delete(p); } catch { }
    }

    private ActionAuditLog Build(AppSettings settings)
    {
        var log = new ActionAuditLog(settings);
        log.OverrideLogPath(_tempFile);
        return log;
    }

    [Fact]
    public void Append_Disabled_ShouldNotWrite()
    {
        var settings = new AppSettings { EnableActionAuditLog = false };
        var log = Build(settings);
        log.Append(new AuditEntry { Action = "force-terminate" });
        Assert.False(File.Exists(_tempFile));
    }

    [Fact]
    public void Append_Enabled_ShouldWriteOneLinePerEntry()
    {
        var settings = new AppSettings { EnableActionAuditLog = true };
        var log = Build(settings);

        for (int i = 0; i < 5; i++)
            log.Append(new AuditEntry
            {
                Action = "close-graceful",
                Pid = 100 + i,
                Name = "notepad.exe",
                Method = "WM_CLOSE",
                Success = true,
                Reason = "ok",
                DurationMs = 100,
                Consent = "checkbox-graceful"
            });

        Assert.True(File.Exists(_tempFile));
        var lines = File.ReadAllLines(_tempFile);
        Assert.Equal(5, lines.Length);

        // 每行可独立 JSON 解析
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal("close-graceful", doc.RootElement.GetProperty("action").GetString());
            Assert.Equal("WM_CLOSE", doc.RootElement.GetProperty("method").GetString());
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public void Append_PrivacyMode_ShouldMaskExeAndFilePath()
    {
        var settings = new AppSettings
        {
            EnableActionAuditLog = true,
            EnablePrivacyMode = true
        };
        var log = Build(settings);

        log.Append(new AuditEntry
        {
            Action = "force-terminate",
            Pid = 9032,
            Name = "notepad.exe",
            Exe = @"C:\Users\Alice\Documents\notepad.exe",
            FilePath = @"E:\secrets\readme.txt",
            Drive = "E:",
            Method = "TerminateProcess",
            Success = true,
            Consent = "type-match-force"
        });

        var json = File.ReadAllText(_tempFile);
        Assert.DoesNotContain("Alice", json);
        Assert.DoesNotContain("secrets", json);
        Assert.DoesNotContain("readme", json);
        Assert.Contains("***", json);
        // 进程名 / 盘符 / 方法 / consent 不脱敏
        Assert.Contains("notepad.exe", json);
        Assert.Contains("\"drive\":\"E:\"", json);
        Assert.Contains("TerminateProcess", json);
        Assert.Contains("type-match-force", json);
    }

    [Fact]
    public void Append_OverSize_ShouldRollFiles()
    {
        var settings = new AppSettings
        {
            EnableActionAuditLog = true,
            AuditLogMaxSizeMB = 1, // 内部下限实际是 1MB；要触发滚动需要 ~1MB 数据
            AuditLogMaxFiles = 3
        };
        var log = Build(settings);

        // 写一条相当大的（每条 ~2KB），重复直到主文件超过 1MB
        // 600 条 × ~2KB = ~1.2MB 应足够触发一次滚动
        var big = new string('x', 1900);
        for (int i = 0; i < 600; i++)
        {
            log.Append(new AuditEntry
            {
                Action = "close-graceful",
                Pid = i,
                Name = "notepad.exe",
                Reason = big,
                Method = "WM_CLOSE",
                Success = true,
                Consent = "checkbox-graceful"
            });
        }

        Assert.True(File.Exists(_tempFile), "主日志文件应存在");
        Assert.True(File.Exists(_tempFile + ".1"), "应已滚动出 .1");
    }

    [Fact]
    public void Append_FailureToWrite_ShouldNotThrow()
    {
        var settings = new AppSettings { EnableActionAuditLog = true };
        var log = new ActionAuditLog(settings);
        // 用一个不可写的路径（非法字符）
        log.OverrideLogPath("???/invalid/" + Guid.NewGuid().ToString("N") + ".log");
        var ex = Record.Exception(() => log.Append(new AuditEntry { Action = "test" }));
        Assert.Null(ex);
    }
}
