@echo off
setlocal
echo ========================================
echo         MDTab-Helper - Launch
echo ========================================
echo.
echo Se lanseaza MDTab-Helper (.NET 8)...
echo.

:: Check if compiled executable exists
if exist "bin\Debug\net8.0-windows\win-x64\MDTab-Helper.exe" (
    start "" "bin\Debug\net8.0-windows\win-x64\MDTab-Helper.exe"
    exit /b 0
)

if exist "bin\Release\net8.0-windows\win-x64\publish\MDTab-Helper.exe" (
    start "" "bin\Release\net8.0-windows\win-x64\publish\MDTab-Helper.exe"
    exit /b 0
)

:: Try running with dotnet
where dotnet >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    dotnet run --project MDHelper.csproj
    exit /b 0
)

:: Check UserProfile .dotnet
if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    "%USERPROFILE%\.dotnet\dotnet.exe" run --project MDHelper.csproj
    exit /b 0
)

echo [EROARE] Nu s-a gasit .NET SDK sau executabilul compilat.
echo Te rog instaleaza .NET 8 SDK de la https://dotnet.microsoft.com/download/dotnet/8.0
pause
exit /b 1
