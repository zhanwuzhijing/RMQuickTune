@echo off
setlocal enabledelayedexpansion
title RMGuide Launcher

rem Go to the directory where this script lives
cd /d "%~dp0"

echo ============================================
echo   赛事引擎向导 RMGuide - RoboMaster Engine Guide
echo ============================================
echo.

rem ---- 1. Prefer a published / built exe (no runtime needed) ----
set "EXE="
for %%P in (
    "src\RMGuide\bin\Release\net8.0-windows\win-x64\publish\RMGuide.exe"
    "src\RMGuide\bin\Release\net8.0-windows\win-x64\RMGuide.exe"
    "src\RMGuide\bin\Debug\net8.0-windows\win-x64\RMGuide.exe"
    "publish\RMGuide.exe"
    "RMGuide.exe"
) do (
    if exist %%P if not defined EXE set "EXE=%%~fP"
)

if defined EXE (
    echo [Start] !EXE!
    start "" "!EXE!"
    goto :done
)

rem ---- 2. No exe found: try running via .NET SDK (dev scenario) ----
echo Built program not found. Trying to run via .NET SDK...
where dotnet >nul 2>nul
if errorlevel 1 (
    echo.
    echo [Error] Program not found and .NET SDK is not installed.
    echo         Publish first:  dotnet publish src\RMGuide -c Release
    echo         Or install .NET 8 SDK and try again.
    echo.
    pause
    exit /b 1
)

echo [Run] dotnet run --project src\RMGuide
dotnet run --project "src\RMGuide" -c Debug
if errorlevel 1 (
    echo.
    echo [Error] Run failed. Please check the output above.
    pause
    exit /b 1
)

:done
endlocal
exit /b 0
