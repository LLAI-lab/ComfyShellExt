@echo off
chcp 65001 >nul
setlocal
title Clear Windows thumbnail cache

echo Windows stores generated thumbnails in a cache, so pictures you have already
echo browsed keep their old look until that cache is dropped.
echo Windows 会缓存已生成的缩略图，清空后才会重新生成并显示 JSON 标记。
echo.
echo This closes Explorer, deletes %%LOCALAPPDATA%%\Microsoft\Windows\Explorer\thumbcache_*.db
echo and iconcache_*.db, then starts Explorer again. The cache rebuilds itself; no
echo document or picture is touched.
echo 该操作只删除可重建的缓存文件，不会影响任何图片或文档。
echo.
choice /C YN /M "Clear the thumbnail cache now / 现在清理缩略图缓存"
if errorlevel 2 goto cancelled

set "CACHE=%LOCALAPPDATA%\Microsoft\Windows\Explorer"
taskkill /F /IM explorer.exe >nul 2>&1
timeout /T 1 /NOBREAK >nul 2>&1
del /F /A /Q "%CACHE%\thumbcache_*.db" 2>nul
del /F /A /Q "%CACHE%\iconcache_*.db" 2>nul
start "" "%WINDIR%\explorer.exe"
echo.
echo Cache cleared / 缓存已清理。
pause
exit /B 0

:cancelled
echo.
echo Cancelled / 已取消，未做任何修改。
pause
exit /B 1
