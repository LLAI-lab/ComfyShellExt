@echo off
chcp 65001 >nul
setlocal
title ComfyShellExt Uninstaller

echo ##################################################################
echo ##  ComfyShellExt - uninstall / 卸载                             ##
echo ##################################################################
echo.

>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"
if '%errorlevel%' NEQ '0' (
    if '%1' EQU 'elevated' (
        echo Cannot elevate. Right click uninstall.bat and pick "Run as administrator".
        echo 无法提权，请右键 uninstall.bat 选择“以管理员身份运行”。
        pause
        exit /B 1
    ) else (
        echo Requesting administrator privileges / 正在请求管理员权限 ...
        echo Set UAC = CreateObject^("Shell.Application"^) > "%temp%\cse_elevate.vbs"
        echo UAC.ShellExecute "%~s0", "elevated", "", "runas", 1 >> "%temp%\cse_elevate.vbs"
        "%temp%\cse_elevate.vbs"
        exit /B
    )
)
if exist "%temp%\cse_elevate.vbs" del "%temp%\cse_elevate.vbs"
cd /d "%~dp0"

set "DLL=%~dp0ComfyShellExt.dll"
set "CLI=%~dp0ComfyWorkflowDb.exe"
set "REGASM64=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
set "REGASM32=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"

if not exist "%DLL%" goto nodll

echo The handlers recorded at install time are restored as we unregister.
echo 卸载会把安装时备份的原始缩略图处理程序恢复回去。
echo.

if not exist "%REGASM32%" goto un64
echo --- unregistering 32-bit ---
"%REGASM32%" /unregister "%DLL%"
:un64
if not exist "%REGASM64%" goto unregistered
echo.
echo --- unregistering 64-bit ---
"%REGASM64%" /unregister "%DLL%"

:unregistered
echo.
if exist "%CLI%" "%CLI%" diag
echo.
choice /C YN /M "Restart Explorer now / 现在重启资源管理器"
if errorlevel 2 goto done
call "%~dp0restart_explorer.bat"

:done
echo.
echo Uninstalled / 卸载完成。缩略图数据库保留在 %%LOCALAPPDATA%%\ComfyShellExt
pause
exit /B 0

:nodll
echo ERROR: ComfyShellExt.dll was not found next to uninstall.bat
echo 错误：未在 uninstall.bat 同目录找到 ComfyShellExt.dll
pause
exit /B 1
