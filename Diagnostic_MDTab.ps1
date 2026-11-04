[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Host.UI.RawUI.WindowTitle = "MDTab-Helper - Diagnosticare Sistem si Conexiune"

Clear-Host
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "             MDTab-Helper - Raport Diagnostic si Conexiune            " -ForegroundColor Yellow
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "======================================================================" -ForegroundColor DarkCyan
Write-Host " 1. TEST CONEXIUNE SI ACCESIBILITATE SERVER (md.b-zone.ro)" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor DarkCyan

$portalUrl = "https://md.b-zone.ro"
try {
    $ping = Test-Connection -ComputerName "md.b-zone.ro" -Count 1 -Quiet -ErrorAction SilentlyContinue
    if ($ping) {
        Write-Host "  [OK] Conexiune ping catre md.b-zone.ro: Functionala." -ForegroundColor Green
    } else {
        Write-Host "  [!] Ping ICMP blocat de Cloudflare (comportament normal pentru protectie B-Zone)." -ForegroundColor DarkYellow
    }
} catch {}

try {
    $req = [System.Net.WebRequest]::Create($portalUrl)
    $req.Timeout = 7000
    $req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130.0.0.0 Safari/537.36"
    $res = $req.GetResponse()
    Write-Host "  [OK] Pagina principala md.b-zone.ro raspunde cu succes (HTTP 200)." -ForegroundColor Green
    $res.Dispose()
} catch {
    Write-Host ("  [EROARE] Nu se poate accesa md.b-zone.ro: " + $_.Exception.Message) -ForegroundColor Red
}

Write-Host ""
Write-Host "======================================================================" -ForegroundColor DarkCyan
Write-Host " 2. TEST STATUS API-URI SI SESIUNE PORTAL (DUTY & BANDAJE)" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor DarkCyan

function Test-ApiEndpoint {
    param([string]$name, [string]$url)
    try {
        $request = [System.Net.HttpWebRequest]::Create($url)
        $request.Timeout = 7000
        $request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130.0.0.0 Safari/537.36"
        $response = $request.GetResponse()
        Write-Host ("  [OK] " + $name + " -> Status: 200 OK (Sesiune activa / date disponibile)") -ForegroundColor Green
        $response.Dispose()
    } catch [System.Net.WebException] {
        $resp = $_.Exception.Response
        if ($resp -ne $null -and [int]$resp.StatusCode -eq 401) {
            Write-Host ("  [!] " + $name + " -> 401 Unauthorized (Sesiune expirata / nelogat)") -ForegroundColor Yellow
            Write-Host "      * Serverul B-Zone refuza accesul fara autentificare." -ForegroundColor DarkYellow
            Write-Host "      * De aceea in tableta apare: 'Sesiune Expirata / Neconectat'!" -ForegroundColor Red
        } else {
            $code = if ($resp -ne $null) { [int]$resp.StatusCode } else { "Eroare Retea" }
            Write-Host ("  [EROARE] " + $name + " -> Status: " + $code + " (" + $_.Exception.Message + ")") -ForegroundColor Red
        }
    } catch {
        Write-Host ("  [EROARE] " + $name + ": " + $_.Exception.Message) -ForegroundColor Red
    }
}

Test-ApiEndpoint "Medici On-Duty  (api/duty/live)" "https://md.b-zone.ro/api/duty/live"
Test-ApiEndpoint "Stoc Bandaje     (api/bandaje)"   "https://md.b-zone.ro/api/bandaje"
Test-ApiEndpoint "Video Homepage   (api/video)"     "https://md.b-zone.ro/api/video"

Write-Host ""
Write-Host "======================================================================" -ForegroundColor DarkCyan
Write-Host " 3. TEST CACHE BROWSER & SESIUNE LOCALA TABLETA" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor DarkCyan

$appData = [Environment]::GetFolderPath("ApplicationData")
$cacheFolder = Join-Path $appData "MDTab-Helper\TabletCache"
$cookieItem = if (Test-Path $cacheFolder) { Get-ChildItem -Path $cacheFolder -Recurse -Filter "Cookies" -File -ErrorAction SilentlyContinue | Select-Object -First 1 } else { $null }

if (Test-Path $cacheFolder) {
    Write-Host ("  [OK] Folderul de cache exista: " + $cacheFolder) -ForegroundColor Green
    if ($cookieItem -ne $null) {
        Write-Host ("  [OK] Baza de date de cookie-uri salvata (" + [Math]::Round($cookieItem.Length / 1KB, 1) + " KB).") -ForegroundColor Green
        Write-Host ("       Ultima actualizare sesiune: " + $cookieItem.LastWriteTime.ToString("dd.MM.yyyy HH:mm:ss")) -ForegroundColor Cyan
    } else {
        Write-Host "  [!] Nu s-au gasit cookie-uri salvate in tableta (inca nu te-ai logat pe md.b-zone.ro)." -ForegroundColor Yellow
    }
} else {
    Write-Host "  [INFO] Folderul de cache nu exista inca." -ForegroundColor Gray
}

Write-Host ""
Write-Host "======================================================================" -ForegroundColor DarkCyan
Write-Host " 4. TEST ANIMATII WINDOWS & MOTOR WEBVIEW2" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor DarkCyan

try {
    $fx = Get-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" -Name "TaskbarAnimations" -ErrorAction SilentlyContinue
    if ($fx -ne $null -and $fx.TaskbarAnimations -eq 0) {
        Write-Host "  [!] Setarea de Windows pentru efecte vizuale este redusa/oprita." -ForegroundColor Yellow
        Write-Host "      -> In MDTab-Helper v2.6.6 animatiile sunt forțate sa ruleze oricum!" -ForegroundColor Cyan
    } else {
        Write-Host "  [OK] Efectele vizuale/animatiile din Windows sunt ACTIVE." -ForegroundColor Green
    }
} catch {}

$dll = "C:\Users\Admin\Documents\Projects\MDTab\bin\Release\net8.0-windows\win-x64\Microsoft.Web.WebView2.Core.dll"
if (Test-Path $dll) {
    try {
        [System.Reflection.Assembly]::LoadFrom($dll) | Out-Null
        $ver = [Microsoft.Web.WebView2.Core.CoreWebView2Environment]::GetAvailableBrowserVersionString()
        Write-Host ("  [OK] Motor WebView2 instalat in Windows: Versiunea " + $ver) -ForegroundColor Green
    } catch {
        Write-Host ("  [!] Nu s-a putut citi versiunea WebView2: " + $_.Exception.Message) -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host " CONCLUZIE SI SOLUTIE RECOMANDATA:" -ForegroundColor Yellow
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host " Daca ai vazut '401 Unauthorized' la testul 2 (Medici On-Duty / Bandaje):" -ForegroundColor White
Write-Host "  -> Totul functioneaza normal pe server, doar ca sesiunea ta a expirat!" -ForegroundColor Cyan
Write-Host "  -> Solutie: Deschide tableta (MDTab-Helper v2.6.6), apasa pe butonul:" -ForegroundColor White
Write-Host "     'Reconectare Portal MD' (sau 'PORTAL ANGAJATI') si logheaza-te." -ForegroundColor Yellow
Write-Host "     Imediat ce te-ai logat, lista si bandajele se incarca automat!" -ForegroundColor Green
Write-Host ""
Write-Host " Optiuni disponibile:" -ForegroundColor DarkCyan
Write-Host "  [1] Deschide md.b-zone.ro in browser pentru verificare"
Write-Host "  [2] Reseteaza memoria cache a tabletei (sterge sesiunile vechi din tableta)"
Write-Host "  [3] Iesire"
Write-Host ""

$choice = Read-Host "Alege o optiune [1, 2 sau 3] (apasa Enter pentru 3)"

if ($choice -eq "1") {
    Start-Process "https://md.b-zone.ro"
    Write-Host "`nPagina a fost deschisa in browserul tau." -ForegroundColor Green
} elseif ($choice -eq "2") {
    Write-Host "`nSe opreste aplicatia MDTab-Helper daca ruleaza..." -ForegroundColor Yellow
    Get-Process -Name "MDTab-Helper","MDTab Helper","MDHelper" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
    if (Test-Path $cacheFolder) {
        Remove-Item -Path $cacheFolder -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "[OK] Memoria cache din tableta a fost stearsa!" -ForegroundColor Green
        Write-Host "La urmatoarea deschidere a tabletei te vei putea loga curat." -ForegroundColor Cyan
    } else {
        Write-Host "[INFO] Nu exista memorie cache de sters." -ForegroundColor Gray
    }
} else {
    Write-Host "`nDiagnosticare finalizata." -ForegroundColor Gray
}

Write-Host "`nApasa orice tasta pentru a inchide aceasta fereastra..." -ForegroundColor DarkGray
try {
    if ([Environment]::UserInteractive) {
        [Console]::ReadKey($true) | Out-Null
    }
} catch {}
