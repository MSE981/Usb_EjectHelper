@echo off
chcp 65001 >nul
cd /d "%~dp0UsbEjectHelper"

:: 检查 .NET SDK
where dotnet >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [错误] 未找到 .NET SDK，请先安装 .NET 8.0 SDK
    echo 下载地址：https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0
    pause
    exit /b 1
)

echo =====================================
echo   USB Eject Helper 启动中...
echo =====================================
echo.

:: 还原包（如果需要）
dotnet restore --nologo --verbosity quiet 2>nul

:: 构建
echo [1/2] 正在编译...
dotnet build --nologo --verbosity quiet 2>nul
if %ERRORLEVEL% neq 0 (
    echo [错误] 编译失败，请检查错误信息。
    pause
    exit /b 1
)

:: 运行
echo [2/2] 正在启动...
echo.
echo 程序已在系统托盘运行，双击托盘图标打开主窗口。
echo 关闭此窗口不会退出程序。
echo.
start "" dotnet run --nologo --project src/UsbEjectHelper

:: 等待一下让程序启动
timeout /t 2 >nul
echo 启动完成！可以关闭此窗口。
pause
