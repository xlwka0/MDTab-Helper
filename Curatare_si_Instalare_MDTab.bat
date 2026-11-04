@echo off
chcp 65001 >nul
title MDTab-Helper - Curatare Versiuni Vechi si Actualizare
color 0B

echo ======================================================================
echo       MDTab-Helper - Curatare Versiuni Vechi si Actualizare
echo ======================================================================
echo.
echo Acest script va:
echo   1. Opreste procesele vechi care ruleaza in fundal (MDHelper / MDTab-Helper).
echo   2. Sterge fisierele vechi si duplicatele din Desktop si Downloads.
echo   3. Descarca automat ultima versiune oficiala (v2.6.6) direct pe Desktop.
echo.
pause

echo.
echo [1/4] Se opresc procesele active pentru a debloca fisierele...
taskkill /F /IM "MDHelper.exe" >nul 2>&1
taskkill /F /IM "MD Helper.exe" >nul 2>&1
taskkill /F /IM "MDTab-Helper.exe" >nul 2>&1
taskkill /F /IM "MDTab Helper.exe" >nul 2>&1
taskkill /F /IM "MDTab.exe" >nul 2>&1
timeout /t 1 /nobreak >nul
echo       -> Toate procesele au fost oprite.

echo.
echo [2/4] Se cauta si se elimina versiunile vechi si duplicatele...

:: Sterge fisiere vechi specifice din Desktop
for %%F in ("%USERPROFILE%\Desktop\MDHelper.exe" "%USERPROFILE%\Desktop\MD Helper.exe" "%USERPROFILE%\Desktop\MDTab Helper.exe" "%USERPROFILE%\Desktop\MDTab.exe") do (
    if exist %%F (
        del /f /q %%F >nul 2>&1
        echo       - Sters fisier vechi: %%~nxF de pe Desktop
    )
)

:: Sterge duplicatele din Downloads (MDTab-Helper (1).exe, MDHelper.exe, etc.)
for %%F in ("%USERPROFILE%\Downloads\MDHelper*.exe" "%USERPROFILE%\Downloads\MD Helper*.exe" "%USERPROFILE%\Downloads\MDTab Helper*.exe" "%USERPROFILE%\Downloads\MDTab-Helper (*).exe") do (
    if exist %%F (
        del /f /q %%F >nul 2>&1
        echo       - Sters duplicat/vechi: %%~nxF din Downloads
    )
)

:: Sterge scurtaturi orfane vechi de pe Desktop
for %%L in ("%USERPROFILE%\Desktop\MDHelper.lnk" "%USERPROFILE%\Desktop\MD Helper.lnk" "%USERPROFILE%\Desktop\MDTab Helper.lnk") do (
    if exist %%L (
        del /f /q %%L >nul 2>&1
        echo       - Stearsa scurtatura veche: %%~nxL
    )
)

echo       -> Curatare finalizata cu succes.

echo.
echo [3/4] Se pregateste folderul dedicat pe Desktop...
set "TARGET_DIR=%USERPROFILE%\Desktop\MDTab-Helper"
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"
set "TARGET_EXE=%TARGET_DIR%\MDTab-Helper.exe"

echo.
echo [4/4] Se descarca ultima versiune oficiala direct de pe GitHub...
echo       URL: https://github.com/xlwka0/MDTab-Helper/releases/latest/download/MDTab-Helper.exe
echo       Destinatie: %TARGET_EXE%
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
    "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13; " ^
    "$url = 'https://github.com/xlwka0/MDTab-Helper/releases/latest/download/MDTab-Helper.exe'; " ^
    "$dest = '%TARGET_EXE%'; " ^
    "try { " ^
    "    Write-Host 'Se descarca fisierul (aprox. 73 MB)...' -ForegroundColor Cyan; " ^
    "    $wc = New-Object System.Net.WebClient; " ^
    "    $wc.Headers.Add('User-Agent', 'MDTab-Helper-CleanInstaller'); " ^
    "    $wc.DownloadFile($url, $dest); " ^
    "    if (Test-Path $dest) { " ^
    "        $ver = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dest).ProductVersion; " ^
    "        Write-Host ('[OK] Descarcare reusita! Versiune instalata: ' + $ver) -ForegroundColor Green; " ^
    "    } else { " ^
    "        Write-Host '[EROARE] Fisierul nu s-a putut salva.' -ForegroundColor Red; " ^
    "    } " ^
    "} catch { " ^
    "    Write-Host ('[EROARE] Descarcarea a esuat: ' + $_.Exception.Message) -ForegroundColor Red; " ^
    "}"

echo.
echo ======================================================================
color 0A
echo   GATA! Versiunea veche a fost stearsa, iar ultima versiune (v2.6.6)
echo   este instalata curat in folderul:
echo   "%TARGET_DIR%\MDTab-Helper.exe"
echo ======================================================================
echo.

set /p launch="Doresti sa pornesti noua versiune acum? [D/N] (Enter = Da): "
if /i "%launch%"=="N" (
    echo.
    pause
    exit /b 0
)

start "" "%TARGET_EXE%"
exit /b 0
