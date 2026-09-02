@echo off
chcp 65001 >nul
setlocal
title ComfyShellExt Installer

echo ##################################################################
echo ##  ComfyShellExt - ComfyUI workflow badge for Explorer          ##
echo ##  在资源管理器缩略图右下角显示 JSON 标记                        ##
echo ##################################################################
echo.

REM --- administrator check, self elevate through a temporary vbs ---
>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"
if '%errorlevel%' NEQ '0' (
    if '%1' EQU 'elevated' (
        echo Cannot elevate. Right click install.bat and pick "Run as administrator".
        echo 无法提权，请右键 install.bat 选择“以管理员身份运行”。
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

echo Folder / 安装目录 : %~dp0
echo Do not move or rename this folder after installing.
echo 安装后请勿移动或重命名此文件夹（注册表记录了当前路径）。
echo.
echo File types come from ComfyShellExt.ini, section [extensions].
echo 处理的文件类型在 ComfyShellExt.ini 的 [extensions] 中配置。
echo.

if not exist "%REGASM64%" goto try32
echo --- registering 64-bit ---
"%REGASM64%" /codebase "%DLL%"
:try32
if not exist "%REGASM32%" goto registered
echo.
echo --- registering 32-bit ---
"%REGASM32%" /codebase "%DLL%"

:registered
echo.
if exist "%CLI%" "%CLI%" diag
echo.
echo Right click entries only appear on files that really carry a workflow. On
echo Windows 11 legacy handlers live under "Show more options" (Shift+right click).
echo 右键的"查看/导出"只在确实含工作流的文件上出现；Win11 需展开"显示更多选项"
echo （或 Shift+右键）。想进一级菜单：把 ini 里 menu.staticverbs 改成 1 后重跑本脚本。
echo.
echo Windows keeps a thumbnail cache, so files you already browsed may keep
echo their old picture until the cache is cleared.
echo 已缓存的缩略图不会立刻变化，必要时运行 clear_thumbnail_cache.bat。
echo.
choice /C YN /M "Restart Explorer now / 现在重启资源管理器"
if errorlevel 2 goto done
call "%~dp0restart_explorer.bat"

:done
echo.
echo Done / 安装完成。
pause
exit /B 0

:nodll
echo ERROR: ComfyShellExt.dll was not found next to install.bat
echo 错误：未在 install.bat 同目录找到 ComfyShellExt.dll
pause
exit /B 1
