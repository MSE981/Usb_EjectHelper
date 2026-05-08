@echo off
rem UsbEjectHelper 启动脚本（cmd 包装器）。
rem 用法：
rem   run.bat            构建并启动 Debug
rem   run.bat -Release   构建并启动 Release
rem   run.bat -Test      跑全部单元测试
rem   run.bat -Build     仅构建，不启动
rem 其它参数透传给 run.ps1
setlocal
set "SCRIPT_DIR=%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%run.ps1" %*
exit /b %ERRORLEVEL%
