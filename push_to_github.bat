@echo off
setlocal
title MDTab-Helper - Scheduled Push to GitHub (13.11.2026 ora 13:30)
cd /d "%~dp0"

echo =======================================================
echo          MDTab-Helper - Push to GitHub (Programat)
echo =======================================================
echo.
echo Data si ora programata: 13.11.2026 ora 13:30
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0push_scheduled.ps1"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [EROARE] A aparut o problema la executia scriptului.
    pause
    exit /b %ERRORLEVEL%
)

exit /b 0
