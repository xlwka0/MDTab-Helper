@echo off
setlocal
echo =======================================================
echo          MDTab-Helper - Push to GitHub
echo =======================================================
echo.
echo Se trimite codul pe https://github.com/xlwka0/MDTab-Helper ...
echo Daca ti se cer datele de conectare GitHub, te rugam sa te autentifici.
echo.

set "GIT_CMD=%LOCALAPPDATA%\Programs\MinGit\cmd\git.exe"
if not exist "%GIT_CMD%" set "GIT_CMD=git"

"%GIT_CMD%" push -u origin main --tags
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [EROARE] Nu s-a putut trimite branch-ul main.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo =======================================================
echo   Gata! Codul a fost trimis pe GitHub!
echo =======================================================
echo.
pause
