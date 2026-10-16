@echo off
setlocal
echo ========================================
echo   MDTab-Helper - Compilare Executabil (.exe)
echo ========================================
echo.

:: Seteaza calea catre dotnet
set "DOTNET_CMD=dotnet"
where dotnet >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
        set "DOTNET_CMD=%USERPROFILE%\.dotnet\dotnet.exe"
    ) else (
        echo [EROARE] Nu s-a gasit .NET SDK.
        echo Te rog instaleaza .NET 8 SDK de la https://dotnet.microsoft.com/download/dotnet/8.0
        pause
        exit /b 1
    )
)

set "TARGET_DIR=%USERPROFILE%\Desktop\MDTab-Helper"

echo [1/2] Se curata si se pregateste folderul destinatie: %TARGET_DIR%...
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"

:: Opreste instanta existenta daca ruleaza in fundal pentru a permite rescrierea
taskkill /F /IM "MDTab-Helper.exe" >nul 2>&1
taskkill /F /IM "MDTab Helper.exe" >nul 2>&1
taskkill /F /IM MDHelper.exe >nul 2>&1

echo [2/2] Se compileaza aplicatia ca Single-File Executable Release...
echo.

"%DOTNET_CMD%" publish "%~dp0MDHelper.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:AllowedReferenceRelatedFileExtensions=none -p:CopyDocumentationFileToOutputDirectory=false -o "%TARGET_DIR%"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [EROARE] Compilarea a esuat! Verifica erorile de mai sus.
    pause
    exit /b %ERRORLEVEL%
)

:: Curata orice fisier auxiliar sau XML/PDB pentru a lasa DOAR executabilul
for %%f in ("%TARGET_DIR%\*.xml" "%TARGET_DIR%\*.pdb" "%TARGET_DIR%\*.ico" "%TARGET_DIR%\*.png") do (
    if exist "%%f" del /f /q "%%f" >nul 2>&1
)
if exist "%TARGET_DIR%\Assets" rmdir /s /q "%TARGET_DIR%\Assets" >nul 2>&1
if exist "%TARGET_DIR%\Old" rmdir /s /q "%TARGET_DIR%\Old" >nul 2>&1
if exist "%TARGET_DIR%\runtimes" rmdir /s /q "%TARGET_DIR%\runtimes" >nul 2>&1

echo.
echo =======================================================
echo   Compilare reusita cu succes!
echo   Executabil unic creat in: %TARGET_DIR%\MDTab-Helper.exe
echo =======================================================
echo.
pause
