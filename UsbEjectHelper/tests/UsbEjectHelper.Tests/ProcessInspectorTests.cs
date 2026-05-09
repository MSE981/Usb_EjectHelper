using Xunit;
using UsbEjectHelper.Core;

namespace UsbEjectHelper.Tests;

/// <summary>
/// ProcessInspector 单元测试。
/// </summary>
public class ProcessInspectorTests
{
    [Theory]
    [InlineData("System", true)]
    [InlineData("csrss.exe", true)]
    [InlineData("wininit.exe", true)]
    [InlineData("services.exe", true)]
    [InlineData("lsass.exe", true)]
    [InlineData("svchost.exe", true)]
    [InlineData("notepad.exe", false)]
    [InlineData("explorer.exe", false)]
    [InlineData("chrome.exe", false)]
    [InlineData("myapp.exe", false)]
    public void IsCriticalProcessName_ShouldMatchExpected(string processName, bool expected)
    {
        var result = ProcessInspector.IsCriticalProcessName(processName);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetCriticalProcessNames_ShouldReturnAll()
    {
        var names = ProcessInspector.GetCriticalProcessNames();
        Assert.NotEmpty(names);
        Assert.Contains("System", names);
        Assert.Contains("csrss.exe", names);
        Assert.Contains("services.exe", names);
    }

    [Fact]
    public void GetProcessInfo_CurrentProcess_ShouldSucceed()
    {
        var inspector = new ProcessInspector();
        var currentPid = Environment.ProcessId;
        var info = inspector.GetProcessInfo(currentPid);
        Assert.NotNull(info);
        Assert.Equal(currentPid, info.Pid);
        Assert.NotEmpty(info.ProcessName);
        Assert.False(info.IsCriticalProcess);
    }

    [Fact]
    public void GetProcessInfo_InvalidPid_ShouldReturnNull()
    {
        var inspector = new ProcessInspector();
        var info = inspector.GetProcessInfo(-1);
        Assert.Null(info);
    }

    [Fact]
    public void GetProcessInfoBatch_ShouldReturnResults()
    {
        var inspector = new ProcessInspector();
        var pids = new[] { Environment.ProcessId };
        var results = inspector.GetProcessInfoBatch(pids);
        Assert.NotEmpty(results);
        Assert.Equal(Environment.ProcessId, results[0].Pid);
    }

    [Fact]
    public void ProcessInfo_RiskLevel_Critical_ShouldBeCorrect()
    {
        var info = new ProcessInfo { ProcessName = "System", RiskTier = ProcessRiskTier.Critical };
        Assert.Equal("系统关键进程", info.RiskLevel);
        Assert.True(info.IsCriticalProcess);
        Assert.False(info.CanTerminate);
    }

    [Fact]
    public void ProcessInfo_RiskLevel_High_ShouldBeCorrect()
    {
        var info = new ProcessInfo { ProcessName = "explorer.exe", RiskTier = ProcessRiskTier.High };
        Assert.Equal("高风险进程", info.RiskLevel);
        Assert.False(info.IsCriticalProcess);
        Assert.True(info.CanTerminate);
    }

    [Fact]
    public void ProcessInfo_RiskLevel_Normal_ShouldBeCorrect()
    {
        var info = new ProcessInfo { ProcessName = "notepad.exe", RiskTier = ProcessRiskTier.Normal };
        Assert.Equal("普通进程", info.RiskLevel);
        Assert.False(info.IsCriticalProcess);
        Assert.True(info.CanTerminate);
    }

    [Theory]
    [InlineData("System", ProcessRiskTier.Critical)]
    [InlineData("csrss", ProcessRiskTier.Critical)]
    [InlineData("csrss.exe", ProcessRiskTier.Critical)]
    [InlineData("Registry", ProcessRiskTier.Critical)]
    [InlineData("explorer", ProcessRiskTier.High)]
    [InlineData("explorer.exe", ProcessRiskTier.High)]
    [InlineData("MsMpEng.exe", ProcessRiskTier.High)]
    [InlineData("OneDrive", ProcessRiskTier.High)]
    [InlineData("notepad", ProcessRiskTier.Normal)]
    [InlineData("notepad.exe", ProcessRiskTier.Normal)]
    [InlineData("chrome", ProcessRiskTier.Normal)]
    [InlineData("", ProcessRiskTier.Normal)]
    public void GetRiskTier_ShouldClassifyCorrectly(string processName, ProcessRiskTier expected)
    {
        Assert.Equal(expected, ProcessInspector.GetRiskTier(processName));
    }

    [Fact]
    public void GetHighRiskProcessNames_ShouldReturnNonEmpty()
    {
        var names = ProcessInspector.GetHighRiskProcessNames();
        Assert.NotEmpty(names);
        Assert.Contains("explorer.exe", names);
        Assert.Contains("MsMpEng.exe", names);
    }
}
