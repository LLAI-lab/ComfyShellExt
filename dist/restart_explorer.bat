@echo off
REM Restarts Explorer so it picks up shell extension changes.
REM 重启资源管理器，使 shell 扩展的改动生效。
taskkill /F /IM explorer.exe >nul 2>&1
taskkill /F /IM dllhost.exe >nul 2>&1
timeout /T 1 /NOBREAK >nul 2>&1
start "" "%WINDIR%\explorer.exe"
exit /B 0
