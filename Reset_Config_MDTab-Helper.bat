@echo off
title MDTab-Helper - Diagnosticare si Resetare Configuratie
color 0B

echo ======================================================================
echo          MDTab-Helper - Diagnosticare si Resetare Configuratie
echo ======================================================================
echo.

if exist "%APPDATA%\MDTab-Helper\config.json" (
    echo [1/3] Configuratie gasita. Analiza parametri existenti:
    echo ----------------------------------------------------------------------
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$p = $env:APPDATA + '\MDTab-Helper\config.json'; try { $c = Get-Content -Raw -Encoding UTF8 $p | ConvertFrom-Json; Write-Host '  * Tema tabletei (tabletTheme): ' -NoNewline -ForegroundColor Cyan; Write-Host $c.keybind.tabletTheme -ForegroundColor Yellow; if ($c.keybind.tabletTheme -eq 'borderless') { Write-Host '    -> EXPLICATIE: Modul este ''borderless'' (fara rama).' -ForegroundColor Red; Write-Host '       In acest mod, bara neagra de jos cu butonul Home era ascunsa din design!' -ForegroundColor Red; } else { Write-Host '    -> Modul este cu rama standard (tablet).' -ForegroundColor Green; } Write-Host '  * Dimensiune fereastra:       ' -NoNewline -ForegroundColor Cyan; Write-Host ($c.keybind.tabletWidth.ToString() + ' x ' + $c.keybind.tabletHeight.ToString() + ' px') -ForegroundColor Yellow; Write-Host '  * Nivel Zoom (tabletZoom):    ' -NoNewline -ForegroundColor Cyan; Write-Host (($c.keybind.tabletZoom * 100).ToString() + '%%') -ForegroundColor Yellow; Write-Host '  * URL de pornire:             ' -NoNewline -ForegroundColor Cyan; Write-Host $c.keybind.tabletUrl -ForegroundColor Yellow; Write-Host '  * Scurtaturi (keybinds):      ' -NoNewline -ForegroundColor Cyan; Write-Host ($c.keybind.entries.Count.ToString() + ' configurate') -ForegroundColor Yellow; } catch { Write-Host ('  [!] Eroare la citire: ' + $_.Exception.Message) -ForegroundColor Red; }"
    echo ----------------------------------------------------------------------
    echo.
    echo O copie a fisierului de configurare a fost salvata pe Desktop:
    echo   "%USERPROFILE%\Desktop\MDTab_Config_Inainte_De_Stergere.json"
    copy /y "%APPDATA%\MDTab-Helper\config.json" "%USERPROFILE%\Desktop\MDTab_Config_Inainte_De_Stergere.json" >nul
    echo.
) else (
    echo [INFO] Nu a fost gasit fisierul de configurare in AppData.
    echo.
)

echo [2/3] Alege actiunea dorita:
echo   [1] Reseteaza DOAR configuratia (pastreaza logarile si conturile din browser)
echo   [2] Resetare COMPLETA din fabrica (sterge configuratia SI memoria cache / logarile)
echo   [3] Anuleaza (nu sterge nimic, am vrut doar sa verific setarile)
echo.
set /p opt="Alege optiunea [1, 2 sau 3] (apasa Enter pentru 1): "

if "%opt%"=="3" (
    echo.
    echo Operatiune anulata. Niciun fisier nu a fost sters.
    echo.
    pause
    exit /b 0
)

echo.
echo [3/3] Se opreste procesul MDTab-Helper...
taskkill /F /IM "MDTab-Helper.exe" >nul 2>&1
taskkill /F /IM "MDTab Helper.exe" >nul 2>&1
taskkill /F /IM "MDHelper.exe" >nul 2>&1
timeout /t 1 /nobreak >nul

if "%opt%"=="2" (
    echo Se efectueaza resetarea completa din fabrica...
    if exist "%APPDATA%\MDTab-Helper" (
        rmdir /s /q "%APPDATA%\MDTab-Helper" >nul 2>&1
    )
    if exist "%LOCALAPPDATA%\MDTab-Helper" (
        rmdir /s /q "%LOCALAPPDATA%\MDTab-Helper" >nul 2>&1
    )
    color 0A
    echo.
    echo ======================================================================
    echo   [SUCCES] Intreaga configuratie si memoria cache au fost sterse!
    echo ======================================================================
) else (
    echo Se sterg fisierele de configurare...
    if exist "%APPDATA%\MDTab-Helper\config.json" del /f /q "%APPDATA%\MDTab-Helper\config.json" >nul 2>&1
    if exist "%APPDATA%\MDTab-Helper\config.json.bak" del /f /q "%APPDATA%\MDTab-Helper\config.json.bak" >nul 2>&1
    del /f /q "%APPDATA%\MDTab-Helper\config.json.corrupt_*" >nul 2>&1
    del /f /q "%APPDATA%\MDTab-Helper\config.json.tmp*" >nul 2>&1
    color 0A
    echo.
    echo ======================================================================
    echo   [SUCCES] Configuratia a fost resetata la valorile implicite din fabrica!
    echo           (Sesiunile si conturile din browser au fost pastrate).
    echo ======================================================================
)

echo.
echo La urmatoarea pornire a MDTab-Helper, se va genera o configuratie curata
echo cu tema standard 'tablet' (care include rama si bara cu butonul de Home).
echo.
pause
