@echo off
title MDTab-Helper - Diagnosticare Sistem si Conexiune
cd /d "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Diagnostic_MDTab.ps1"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo A aparut o eroare la rularea diagnosticului.
    pause
)
