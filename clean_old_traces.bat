@echo off
chcp 65001 >nul
title MDTab-Helper - Curățare Urme Programe Vechi
color 0C

echo ======================================================================
echo          MDTab-Helper - Curățare Completă Urme Programe Vechi
echo ======================================================================
echo.
echo Acest script va elimina în siguranță orice urmă din sistem pentru
echo toate denumirile vechi folosite anterior:
echo.
echo  • MDHelper / MD Helper
echo  • MDTab (versiunea veche)
echo  • MDTab Helper (fără cratimă) / MDTab.Helper
echo  • MDTab-Helper-Old
echo  • MD Utilities
echo  • B-ZONE MDH / B-Zone MDHelper
echo.
echo NOTĂ: Versiunea curentă 'MDTab-Helper' (cu cratimă) și proiectul
echo       vor rămâne 100%% INTACTE!
echo.
echo Apasă orice tastă pentru a începe curățarea...
pause >nul
echo.

echo [1/6] Oprire procese vechi care ar putea rula în fundal...
for %%P in ("MDHelper.exe" "MD Helper.exe" "MDTab.exe" "MDTab Helper.exe" "MDTab.Helper.exe" "MD Utilities.exe" "B-ZONE MDH.exe" "B-Zone MDHelper.exe") do (
    taskkill /F /IM %%P >nul 2>&1
)

echo [2/6] Curățare foldere din AppData (Roaming și Local)...
for %%N in ("MDHelper" "MD Helper" "MDTab" "MDTab Helper" "MDTab.Helper" "MD Utilities" "B-ZONE MDH" "B-Zone MDHelper") do (
    if exist "%APPDATA%\%%~N" (
        echo   - Se șterge: %APPDATA%\%%~N
        rmdir /s /q "%APPDATA%\%%~N" >nul 2>&1
    )
    if exist "%LOCALAPPDATA%\%%~N" (
        echo   - Se șterge: %LOCALAPPDATA%\%%~N
        rmdir /s /q "%LOCALAPPDATA%\%%~N" >nul 2>&1
    )
)

echo [3/6] Curățare foldere, scurtături și fișiere vechi de pe Desktop...
for %%N in ("MDHelper" "MD Helper" "MDTab" "MDTab Helper" "MDTab.Helper" "MD Utilities" "B-ZONE MDH" "B-Zone MDHelper" "MDTab-Helper-Old") do (
    if exist "%USERPROFILE%\Desktop\%%~N" (
        echo   - Se șterge folder Desktop: %%~N
        rmdir /s /q "%USERPROFILE%\Desktop\%%~N" >nul 2>&1
    )
    if exist "%USERPROFILE%\Desktop\%%~N.lnk" (
        echo   - Se șterge scurtătură Desktop: %%~N.lnk
        del /f /q "%USERPROFILE%\Desktop\%%~N.lnk" >nul 2>&1
    )
    if exist "%USERPROFILE%\Desktop\%%~N.exe" (
        echo   - Se șterge executabil Desktop: %%~N.exe
        del /f /q "%USERPROFILE%\Desktop\%%~N.exe" >nul 2>&1
    )
    if exist "%PUBLIC%\Desktop\%%~N.lnk" (
        del /f /q "%PUBLIC%\Desktop\%%~N.lnk" >nul 2>&1
    )
)

echo [4/6] Curățare intrări Startup din Windows Registry...
for %%N in ("MDHelper" "MD Helper" "MDTab" "MDTab Helper" "MDTab.Helper" "MD Utilities" "B-ZONE MDH" "B-Zone MDHelper" "MDTab-Helper-Old") do (
    reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v %%N /f >nul 2>&1
    reg delete "HKLM\Software\Microsoft\Windows\CurrentVersion\Run" /v %%N /f >nul 2>&1
)

echo [5/6] Curățare scurtături orfane din folderele Windows Startup...
for %%N in ("*MDHelper*" "*MD Helper*" "*MD Utilities*" "*B-ZONE MDH*") do (
    del /f /q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\%%~N" >nul 2>&1
    del /f /q "%PROGRAMDATA%\Microsoft\Windows\Start Menu\Programs\Startup\%%~N" >nul 2>&1
)
powershell -NoProfile -Command "Get-ChildItem -Path @(\"$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\", \"$env:PROGRAMDATA\Microsoft\Windows\Start Menu\Programs\Startup\") -ErrorAction SilentlyContinue | Where-Object { ($_.Name -like '*MDTab*' -or $_.Name -like '*MD Helper*') -and $_.Name -notlike '*MDTab-Helper*' } | Remove-Item -Force -ErrorAction SilentlyContinue" >nul 2>&1

echo [6/6] Curățare chei software și fișiere temporare vechi...
for %%N in ("MDHelper" "MD Helper" "MDTab" "MDTab Helper" "MDTab.Helper" "MD Utilities" "B-ZONE MDH") do (
    reg delete "HKCU\Software\%%~N" /f >nul 2>&1
)
del /f /q "%TEMP%\mdhelper_*.bat" >nul 2>&1
del /f /q "%TEMP%\MDHelper*.tmp" >nul 2>&1
del /f /q "%TEMP%\MDTab Helper*.tmp" >nul 2>&1

color 0A
echo.
echo ======================================================================
echo   SUCCES! Toate urmele versiunilor vechi au fost eliminate din sistem.
echo   Versiunea oficială rămasă activă este: MDTab-Helper
echo ======================================================================
echo.
pause
