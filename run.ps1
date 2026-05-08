<#
.SYNOPSIS
    UsbEjectHelper 启动 / 构建 / 测试脚本（PowerShell）。

.DESCRIPTION
    无参数时：构建并启动 Debug 版主程序（系统托盘图标会出现，主窗口默认最小化到托盘）。
    支持的开关：
        -Release   使用 Release 配置而不是 Debug
        -Build     仅构建，不启动
        -Test      跑全部 xunit 单元测试，不启动
        -Clean     先 clean 再继续后续动作
        -Pretty    在控制台中以更清晰的颜色打印日志（仅影响脚本输出，不影响程序）

.EXAMPLES
    .\run.ps1
    .\run.ps1 -Release
    .\run.ps1 -Test
    .\run.ps1 -Clean -Build
#>
[CmdletBinding()]
param(
    [switch]$Release,
    [switch]$Build,
    [switch]$Test,
    [switch]$Clean,
    [switch]$Pretty
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Solution   = Join-Path $ScriptRoot 'UsbEjectHelper\UsbEjectHelper.sln'
$AppProject = Join-Path $ScriptRoot 'UsbEjectHelper\src\UsbEjectHelper\UsbEjectHelper.csproj'
$Configuration = if ($Release) { 'Release' } else { 'Debug' }

function Write-Step($msg) {
    if ($Pretty) { Write-Host "==> $msg" -ForegroundColor Cyan }
    else        { Write-Host "==> $msg" }
}

function Assert-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $cmd) {
        Write-Error '未检测到 dotnet。请先安装 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0'
    }
}

Assert-Dotnet

if ($Clean) {
    Write-Step "Clean ($Configuration)"
    dotnet clean $Solution -c $Configuration --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($Test) {
    Write-Step "Test ($Configuration)"
    dotnet test $Solution -c $Configuration --nologo
    exit $LASTEXITCODE
}

Write-Step "Build ($Configuration)"
dotnet build $Solution -c $Configuration --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($Build) {
    Write-Step '仅构建模式，已完成。'
    exit 0
}

Write-Step "Run ($Configuration)"
Write-Host '提示：程序为系统托盘常驻应用。如未弹出主窗口，请查看任务栏右下角的托盘图标。'
dotnet run --project $AppProject -c $Configuration --no-build
exit $LASTEXITCODE
