using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MDHelper.Services;

public record UpdateInfo
{
    public bool IsUpdateAvailable { get; init; }
    public string CurrentVersion { get; init; } = "";
    public string NewVersion { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public long FileSizeBytes { get; init; }
    public string Changelog { get; init; } = "";
    public DateTime? PublishedAt { get; init; }
}

public class UpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/xlwka0/MDTab-Helper/releases/latest";
    private readonly HttpClient _httpClient;

    public UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MDTab-Helper-Updater/2.5");
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    public static string GetCurrentVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version;
        if (ver != null)
        {
            return $"{ver.Major}.{ver.Minor}.{ver.Build}";
        }
        return "2.5.0";
    }

    public async Task<UpdateInfo> CheckForUpdatesAsync()
    {
        string currentVerStr = GetCurrentVersion();

        try
        {
            var response = await _httpClient.GetAsync(GitHubApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateInfo { IsUpdateAvailable = false, CurrentVersion = currentVerStr };
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            string changelog = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
            DateTime? publishedAt = root.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTime(out var dt) ? dt : null;

            // Search for asset "MDTab Helper.exe" (or any .exe)
            string downloadUrl = "";
            long sizeBytes = 0;

            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                        sizeBytes = asset.TryGetProperty("size", out var sizeProp) ? sizeProp.GetInt64() : 0;
                        if (name.Equals("MDTab-Helper.exe", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("MDTab Helper.exe", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("MDTab.Helper.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            break; // exact preferred match
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(downloadUrl) || string.IsNullOrEmpty(tagName))
            {
                return new UpdateInfo { IsUpdateAvailable = false, CurrentVersion = currentVerStr };
            }

            string cleanTag = tagName.TrimStart('v', 'V');
            if (IsVersionNewer(cleanTag, currentVerStr))
            {
                return new UpdateInfo
                {
                    IsUpdateAvailable = true,
                    CurrentVersion = currentVerStr,
                    NewVersion = cleanTag,
                    DownloadUrl = downloadUrl,
                    FileSizeBytes = sizeBytes,
                    Changelog = changelog,
                    PublishedAt = publishedAt
                };
            }
        }
        catch
        {
            // Network errors or repository unreachable (e.g. offline)
        }

        return new UpdateInfo { IsUpdateAvailable = false, CurrentVersion = currentVerStr };
    }

    public async Task<string> DownloadUpdateAsync(string downloadUrl, IProgress<(long downloaded, long total)>? progress, CancellationToken ct = default)
    {
        string currentExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MDTab-Helper.exe");
        string appDir = Path.GetDirectoryName(currentExe) ?? Path.GetTempPath();

        string tempFile;
        try
        {
            tempFile = Path.Combine(appDir, $"MDTab-Helper.update_{Guid.NewGuid():N}.tmp");
            using (var fs = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            File.Delete(tempFile);
        }
        catch
        {
            tempFile = Path.Combine(Path.GetTempPath(), $"MDTab-Helper.update_{Guid.NewGuid():N}.tmp");
        }

        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? -1L;
        long totalRead = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        byte[] buffer = new byte[81920];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;
            progress?.Report((totalRead, totalBytes));
        }

        return tempFile;
    }

    public static void ApplyUpdateAndRestart(string tempDownloadedExe)
    {
        string currentExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MDTab-Helper.exe");
        string backupExe = currentExe + ".old";

        try
        {
            // 1. Delete previous .old backup if lingering
            if (File.Exists(backupExe))
            {
                try { File.Delete(backupExe); } catch { }
            }

            // 2. Windows NTFS allows renaming the currently running executable directly!
            File.Move(currentExe, backupExe, overwrite: true);

            // 3. Move the downloaded update directly into the current executable path
            try
            {
                // File.Copy works seamlessly across different disk drives/volumes
                File.Copy(tempDownloadedExe, currentExe, overwrite: true);
                try { File.Delete(tempDownloadedExe); } catch { }
            }
            catch
            {
                // Rollback: if placing the new file failed, restore original executable
                if (!File.Exists(currentExe) && File.Exists(backupExe))
                {
                    try { File.Move(backupExe, currentExe); } catch { }
                }
                throw;
            }

            // 4. Launch the updated executable directly without any intermediate batch scripts
            var startInfo = new ProcessStartInfo
            {
                FileName = currentExe,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(currentExe) ?? ""
            };

            // Microsoft Defender Cloud Security Scan can hold the new file for up to 10 seconds.
            // Retry launching gracefully until Defender scan finishes.
            Process? newProcess = null;
            for (int attempt = 0; attempt < 15; attempt++)
            {
                try
                {
                    newProcess = Process.Start(startInfo);
                    if (newProcess != null) break;
                }
                catch
                {
                    Thread.Sleep(1000);
                }
            }

            if (newProcess == null)
            {
                throw new InvalidOperationException("Process launch timed out during security scan.");
            }

            // 5. Cleanly shut down this older instance
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                System.Windows.Application.Current.Shutdown();
            });
            return;
        }
        catch
        {
            // If direct atomic rename failed (e.g. cross-volume or special permissions), fallback to script
        }

        int currentPid = Environment.ProcessId;
        string batPath = Path.Combine(Path.GetTempPath(), $"mdtab_updater_{Guid.NewGuid():N}.bat");

        string batContent = $@"@echo off
setlocal
set RETRIES=0

:WAIT_LOOP
tasklist /fi ""PID eq {currentPid}"" 2>nul | find ""{currentPid}"" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    set /a RETRIES+=1
    if %RETRIES% lss 15 goto WAIT_LOOP
)

if exist ""{tempDownloadedExe}"" (
    copy /y ""{tempDownloadedExe}"" ""{currentExe}"" >nul
    del /f /q ""{tempDownloadedExe}"" >nul
)
start """" ""{currentExe}""
del /f /q ""%~f0""
exit
";

        File.WriteAllText(batPath, batContent);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batPath}\"",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = true
        };

        Process.Start(psi);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            System.Windows.Application.Current.Shutdown();
        });
    }

    public static void CleanupOldUpdateFiles()
    {
        try
        {
            string currentExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MDTab-Helper.exe");
            string backupExe = currentExe + ".old";
            if (File.Exists(backupExe))
            {
                try { File.Delete(backupExe); } catch { }
            }

            string appDir = Path.GetDirectoryName(currentExe) ?? "";
            if (!string.IsNullOrEmpty(appDir) && Directory.Exists(appDir))
            {
                foreach (var f in Directory.GetFiles(appDir, "MDTab-Helper.update_*.tmp"))
                {
                    try { File.Delete(f); } catch { }
                }
            }
        }
        catch { }
    }

    private static bool IsVersionNewer(string remoteVersion, string localVersion)
    {
        if (Version.TryParse(remoteVersion, out var remote) && Version.TryParse(localVersion, out var local))
        {
            return remote > local;
        }

        // Fallback simple comparison if version contains extra dots
        var rParts = remoteVersion.Split('.');
        var lParts = localVersion.Split('.');
        int length = Math.Max(rParts.Length, lParts.Length);

        for (int i = 0; i < length; i++)
        {
            int r = i < rParts.Length && int.TryParse(rParts[i], out var rv) ? rv : 0;
            int l = i < lParts.Length && int.TryParse(lParts[i], out var lv) ? lv : 0;
            if (r > l) return true;
            if (r < l) return false;
        }

        return false;
    }
}
