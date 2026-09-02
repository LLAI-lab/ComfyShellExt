@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
cd /d "%~dp0"
title Build ComfyShellExt

set "OUT=%~dp0dist"
set "LIBRSP=%TEMP%\comfyshellext_lib.rsp"
set "EXERSP=%TEMP%\comfyshellext_exe.rsp"

REM --- reference assemblies -------------------------------------------------
set "REFDIR="
for %%v in (v4.8.1 v4.8 v4.7.2) do (
  if not defined REFDIR if exist "%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\%%v\mscorlib.dll" (
    set "REFDIR=%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\%%v"
  )
)
if not defined REFDIR set "REFDIR=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"

REM --- C# compiler: prefer the Roslyn one shipped with Visual Studio --------
set "CSC="
for %%e in (Community Professional Enterprise BuildTools Preview) do (
  if not defined CSC if exist "%ProgramFiles%\Microsoft Visual Studio\2022\%%e\MSBuild\Current\Bin\Roslyn\csc.exe" (
    set "CSC=%ProgramFiles%\Microsoft Visual Studio\2022\%%e\MSBuild\Current\Bin\Roslyn\csc.exe"
  )
)
if not defined CSC if exist "%ProgramFiles(x86)%\MSBuild\14.0\Bin\csc.exe" set "CSC=%ProgramFiles(x86)%\MSBuild\14.0\Bin\csc.exe"
if not defined CSC set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" goto nocsc

echo compiler   : %CSC%
echo references : %REFDIR%
echo output     : %OUT%
echo.
if not exist "%OUT%" mkdir "%OUT%"

REM --- ComfyShellExt.dll (core + shell extension) ---------------------------
> "%LIBRSP%" echo -nologo
>>"%LIBRSP%" echo -target:library
>>"%LIBRSP%" echo -langversion:7.3
>>"%LIBRSP%" echo -platform:anycpu
>>"%LIBRSP%" echo -optimize+
>>"%LIBRSP%" echo -debug:pdbonly
>>"%LIBRSP%" echo -nostdlib+
>>"%LIBRSP%" echo -out:"%OUT%\ComfyShellExt.dll"
for %%r in (mscorlib System System.Core System.Drawing) do >>"%LIBRSP%" echo -reference:"%REFDIR%\%%r.dll"
for /r "%~dp0src\Core" %%f in (*.cs) do >>"%LIBRSP%" echo "%%f"
for /r "%~dp0src\Shell" %%f in (*.cs) do >>"%LIBRSP%" echo "%%f"
echo --- ComfyShellExt.dll
"%CSC%" -noconfig @"%LIBRSP%"
if errorlevel 1 goto failed

REM --- ComfyWorkflowDb.exe (scanner and diagnostics) ------------------------
> "%EXERSP%" echo -nologo
>>"%EXERSP%" echo -target:exe
>>"%EXERSP%" echo -langversion:7.3
>>"%EXERSP%" echo -platform:anycpu
>>"%EXERSP%" echo -optimize+
>>"%EXERSP%" echo -debug:pdbonly
>>"%EXERSP%" echo -nostdlib+
>>"%EXERSP%" echo -out:"%OUT%\ComfyWorkflowDb.exe"
for %%r in (mscorlib System System.Core System.Drawing System.Windows.Forms) do >>"%EXERSP%" echo -reference:"%REFDIR%\%%r.dll"
>>"%EXERSP%" echo -reference:"%OUT%\ComfyShellExt.dll"
for /r "%~dp0src\Cli" %%f in (*.cs) do >>"%EXERSP%" echo "%%f"
echo --- ComfyWorkflowDb.exe
"%CSC%" -noconfig @"%EXERSP%"
if errorlevel 1 goto failed

REM --- ComfyWorkflowMenu.exe (what the right click entries launch) ------------
set "MENURSP=%TEMP%\comfyshellext_menu.rsp"
> "%MENURSP%" echo -nologo
>>"%MENURSP%" echo -target:winexe
>>"%MENURSP%" echo -langversion:7.3
>>"%MENURSP%" echo -platform:anycpu
>>"%MENURSP%" echo -optimize+
>>"%MENURSP%" echo -debug:pdbonly
>>"%MENURSP%" echo -nostdlib+
>>"%MENURSP%" echo -out:"%OUT%\ComfyWorkflowMenu.exe"
for %%r in (mscorlib System System.Core System.Drawing System.Windows.Forms) do >>"%MENURSP%" echo -reference:"%REFDIR%\%%r.dll"
>>"%MENURSP%" echo -reference:"%OUT%\ComfyShellExt.dll"
for /r "%~dp0src\Menu" %%f in (*.cs) do >>"%MENURSP%" echo "%%f"
echo --- ComfyWorkflowMenu.exe
"%CSC%" -noconfig @"%MENURSP%"
if errorlevel 1 goto failed
del "%MENURSP%" 2>nul

del "%LIBRSP%" "%EXERSP%" 2>nul
echo.
echo Build finished / 编译完成:
dir /b "%OUT%\ComfyShellExt.dll" "%OUT%\ComfyWorkflowDb.exe" "%OUT%\ComfyWorkflowMenu.exe"
echo.
echo Next: run dist\install.bat as administrator.
echo 下一步：以管理员身份运行 dist\install.bat。
exit /B 0

:failed
echo.
echo BUILD FAILED / 编译失败
echo If the compiler is the in-box .NET Framework csc.exe it only speaks C# 5;
echo install Visual Studio 2022 Build Tools, or use the prebuilt dist folder.
exit /B 1

:nocsc
echo No C# compiler found / 未找到 C# 编译器 (csc.exe)
exit /B 1
