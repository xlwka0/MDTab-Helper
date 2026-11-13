@echo off
setlocal
title MDTab-Helper - Push to GitHub
cd /d "%~dp0"

echo =======================================================
echo          MDTab-Helper - Push to GitHub
echo =======================================================
echo.
echo Se pregateste trimiterea codului pe https://github.com/xlwka0/MDTab-Helper-Source ...
echo.

set "GIT_CMD=%LOCALAPPDATA%\Programs\MinGit\cmd\git.exe"
if not exist "%GIT_CMD%" set "GIT_CMD=git"

"%GIT_CMD%" add -A
"%GIT_CMD%" diff --cached --quiet
if %ERRORLEVEL% NEQ 0 (
    echo Exista modificari locale detectate. Se creeaza commit...
    "%GIT_CMD%" commit -m "Update MDTab-Helper codebase"
) else (
    echo Nu exista fisiere modificate necomise.
)

echo.
echo Se trimite branch-ul main pe origin...
"%GIT_CMD%" push -u origin main --tags
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [EROARE] Nu s-a putut trimite pe GitHub. Verifica conexiunea sau datele de conectare.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo =======================================================
echo   Gata! Codul a fost trimis pe GitHub cu succes!
echo =======================================================
echo.
pause
