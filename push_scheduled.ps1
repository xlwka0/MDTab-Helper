# =======================================================
#    MDTab-Helper - Scheduled Push to GitHub
#    Target Date & Time: 13.11.2026 ora 13:30
# =======================================================

$targetTime = [DateTime]::Parse("2026-11-13 13:30:00")
$projectDir = "C:\Users\Admin\Documents\Projects\MDTab"
Set-Location $projectDir

$gitCmd = "$env:LOCALAPPDATA\Programs\MinGit\cmd\git.exe"
if (-not (Test-Path $gitCmd)) {
    $gitCmd = "git"
}

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "        MDTab-Helper - Programare Push GitHub          " -ForegroundColor Yellow
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "Data si ora tinta : 13.11.2026 ora 13:30" -ForegroundColor White
Write-Host "Director proiect  : $projectDir" -ForegroundColor Gray
Write-Host "Remote tinta      : origin (https://github.com/xlwka0/MDTab-Helper-Source)" -ForegroundColor Gray
Write-Host "=======================================================" -ForegroundColor Cyan

# Test if Console key reading is available
$canReadKey = $false
try {
    $canReadKey = [System.Console]::KeyAvailable -ne $null
} catch {
    $canReadKey = $false
}

$now = [DateTime]::Now
if ($now -lt $targetTime) {
    Write-Host ""
    if ($canReadKey) {
        Write-Host "Astept pana la ora 13:30... (Apasa tasta 'F' pentru a trimite ACUM)" -ForegroundColor Green
    } else {
        Write-Host "Astept in fundal pana la ora 13:30..." -ForegroundColor Green
    }
    Write-Host "(Aceasta fereastra poate fi minimizata in taskbar)" -ForegroundColor Gray
    Write-Host ""

    while ([DateTime]::Now -lt $targetTime) {
        $remaining = $targetTime - [DateTime]::Now
        $formatted = "{0:D2}h {1:D2}m {2:D2}s" -f [int]$remaining.TotalHours, $remaining.Minutes, $remaining.Seconds
        
        try {
            $host.UI.RawUI.WindowTitle = "MDTab-Helper Push: In asteptare pana la 13:30 ($formatted ramas)"
        } catch { }

        Write-Host "`r[Timp ramas: $formatted] In asteptare pana la 13:30..." -NoNewline -ForegroundColor Gray

        if ($canReadKey) {
            try {
                if ([System.Console]::KeyAvailable) {
                    $key = [System.Console]::ReadKey($true)
                    if ($key.Key -eq [ConsoleKey]::F -or $key.Key -eq [ConsoleKey]::Enter) {
                        Write-Host ""
                        Write-Host ""
                        Write-Host "[FORTAT] Trimitere declansata manual de utilizator!" -ForegroundColor Yellow
                        break
                    }
                }
            } catch { }
        }

        Start-Sleep -Milliseconds 1000
    }
    Write-Host ""
}

Write-Host ""
Write-Host "Ora 13:30 a sosit sau trimiterea a fost declansata!" -ForegroundColor Green
Write-Host "Se pregateste trimiterea codului pe GitHub..." -ForegroundColor Cyan

# Stage all files
& $gitCmd add -A

# Check if there are changes to commit
& $gitCmd diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "Exista modificari locale detectate. Se creeaza commit..." -ForegroundColor Yellow
    & $gitCmd commit -m "Auto-scheduled update: UI layout, headless AppController, and feedback optimization"
} else {
    Write-Host "Nu exista modificari noi necomise. Se verifica branch-ul..." -ForegroundColor Gray
}

Write-Host "Se trimite pe GitHub (origin main --tags)..." -ForegroundColor Cyan
& $gitCmd push -u origin main --tags

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "=======================================================" -ForegroundColor Green
    Write-Host "  SUCCES! Codul a fost trimis pe GitHub cu succes!     " -ForegroundColor Green
    Write-Host "=======================================================" -ForegroundColor Green

    # Cleanup startup shortcut and scheduled task
    try {
        $startupLnk = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\MDTab_Push_To_GitHub.lnk"
        if (Test-Path $startupLnk) {
            Remove-Item $startupLnk -Force -ErrorAction SilentlyContinue
            Write-Host "- Scurtatura de pornire automata a fost eliminata din Startup." -ForegroundColor Gray
        }
        Unregister-ScheduledTask -TaskName "MDTab_Push_GitHub_Tomorrow" -Confirm:$false -ErrorAction SilentlyContinue
        Write-Host "- Task-ul programat din Windows Task Scheduler a fost sters." -ForegroundColor Gray
    } catch { }
} else {
    Write-Host ""
    Write-Host "=======================================================" -ForegroundColor Red
    Write-Host "  [EROARE] Trimiterea pe GitHub a esuat!               " -ForegroundColor Red
    Write-Host "  Verifica conexiunea la internet sau starea Git.      " -ForegroundColor Red
    Write-Host "=======================================================" -ForegroundColor Red
}

Write-Host ""
if ($canReadKey) {
    Write-Host "Apasa orice tasta pentru a inchide fereastra..." -ForegroundColor Gray
    try { [System.Console]::ReadKey($true) | Out-Null } catch { }
} else {
    Start-Sleep -Seconds 10
}
