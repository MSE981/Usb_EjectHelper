@echo off
chcp 65001 >nul
cd /d "%~dp0"

where dotnet >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [错误] 未找到 .NET SDK，请先安装 .NET 8.0 SDK
    pause
    exit /b 1
)

echo =====================================
echo   USB Eject Helper 发布打包
echo =====================================
echo.

set PUBLISH_DIR=%~dp0publish

echo [1/3] 正在发布（单文件、自包含）...
dotnet publish src/UsbEjectHelper/UsbEjectHelper.csproj ^
    --output "%PUBLISH_DIR%" ^
    -c Release ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    --self-contained true ^
    -r win-x64 ^
    --nologo

if %ERRORLEVEL% neq 0 (
    echo [错误] 发布失败。
    pause
    exit /b 1
)

echo [2/3] 复制启动脚本...
copy /y "%~dp0启动UsbEjectHelper.bat" "%PUBLISH_DIR%\" >nul

echo [3/3] 完成！
echo.
echo =====================================
echo   发布成功！
echo   双击 publish\UsbEjectHelper.exe 即可运行
echo   位置：%PUBLISH_DIR%
echo =====================================
echo.
pause
