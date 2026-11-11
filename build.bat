@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul
title AgentScreenGuard 构建脚本

echo ==============================================================================
echo                 AgentScreenGuard - 纯原生轻量化构建系统
echo ==============================================================================
echo.

:: 查找系统原生 C# 编译器 csc.exe
set "CSC_EXE="
if exist "%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" (
    set "CSC_EXE=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
) else if exist "%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe" (
    set "CSC_EXE=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

if "%CSC_EXE%"=="" (
    echo [!] 错误: 未在系统中检测到 .NET Framework 4.0+ 编译器 csc.exe
    pause
    exit /b 1
)

echo [*] 找到原生编译器: %CSC_EXE%
echo [*] 正在准备输出目录...
if not exist "%~dp0bin" mkdir "%~dp0bin"

set "OUT_EXE=%~dp0bin\AgentScreenGuard.exe"
set "SRC_DIR=%~dp0src"
set "MANIFEST=%SRC_DIR%\app.manifest"

set "ICON_ARG="
if exist "%SRC_DIR%\app.ico" set ICON_ARG=/win32icon:"%SRC_DIR%\app.ico"

echo [*] 正在执行无依赖高保真编译 (Per-Monitor V2 DPI, Target: Winexe)...
"%CSC_EXE%" /nologo /codepage:65001 /utf8output /t:winexe /optimize+ /platform:anycpu /appconfig:"%SRC_DIR%\app.config" /win32manifest:"%MANIFEST%" %ICON_ARG% /out:"%OUT_EXE%" /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll,System.Management.dll "%SRC_DIR%\*.cs"

if %errorlevel% neq 0 (
    echo [!] 编译失败，请检查源代码语法或依赖引用。
    pause
    exit /b %errorlevel%
)

echo.
echo [√] 构建成功: %OUT_EXE%
for %%I in ("%OUT_EXE%") do echo [*] 二进制文件大小: %%~zI 字节 - 极致轻量
echo ==============================================================================
echo [√] 全部完成！可以直接运行 bin\AgentScreenGuard.exe

