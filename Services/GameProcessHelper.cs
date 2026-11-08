using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace MDHelper.Services;

/// <summary>
/// Helper to detect whether the target game (GTA V / FiveM) is running,
/// actively connected to a server (not just in main menu / launcher), and/or active foreground.
/// </summary>
public static class GameProcessHelper
{
    private static readonly string[] GameProcessPrefixes = new[]
    {
        "gta5",
        "fivem"
    };

    private static readonly string[] GameWindowKeywords = new[]
    {
        "Grand Theft Auto",
        "FiveM"
    };

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    private static bool _cachedIsRunning = false;
    private static DateTime _lastRunningCheck = DateTime.MinValue;
    private static readonly object _runningLock = new();

    /// <summary>
    /// Checks whether the game window is currently the active foreground window,
    /// and that the game is actively connected to a server.
    /// </summary>
    public static bool IsGameForeground()
    {
        // Keybinds and actions only apply when in an active game session
        if (!IsGameRunning()) return false;

        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        // 1. Process name check
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != 0)
        {
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                string procName = proc.ProcessName;
                foreach (var prefix in GameProcessPrefixes)
                {
                    if (procName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Process may have exited or access denied
            }
        }

        // 2. Window title check as fallback
        try
        {
            var sb = new StringBuilder(256);
            if (GetWindowText(hwnd, sb, sb.Capacity) > 0)
            {
                string title = sb.ToString();
                foreach (var kw in GameWindowKeywords)
                {
                    if (title.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    /// <summary>
    /// Checks whether the game is running AND actively connected to a server (not sitting in launcher/menu).
    /// Results are cached for up to 1 second to minimize performance impact.
    /// </summary>
    public static bool IsGameRunning(bool bypassCache = false)
    {
        lock (_runningLock)
        {
            var now = DateTime.UtcNow;
            if (!bypassCache && (now - _lastRunningCheck).TotalSeconds < 1.0)
            {
                return _cachedIsRunning;
            }

            _lastRunningCheck = now;
            _cachedIsRunning = CheckIsGameRunningInternal();
            return _cachedIsRunning;
        }
    }

    private static bool CheckIsGameRunningInternal()
    {
        // 1. Check for standard GTA5 in-game process
        try
        {
            var gtaProcs = Process.GetProcessesByName("GTA5");
            if (gtaProcs != null && gtaProcs.Length > 0)
            {
                foreach (var p in gtaProcs) p.Dispose();
                return true;
            }
        }
        catch { }

        try
        {
            var gtaProcs = Process.GetProcessesByName("gta5");
            if (gtaProcs != null && gtaProcs.Length > 0)
            {
                foreach (var p in gtaProcs) p.Dispose();
                return true;
            }
        }
        catch { }

        // 2. Check for FiveM: must be actively connected to a server (not just in main menu)
        bool hasFiveMProcess = false;
        try
        {
            var procs = Process.GetProcesses();
            foreach (var p in procs)
            {
                try
                {
                    if (p.ProcessName.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase))
                    {
                        hasFiveMProcess = true;
                        break;
                    }
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch { }

        if (hasFiveMProcess)
        {
            return IsFiveMConnectedToServer();
        }

        return false;
    }

    private static bool IsFiveMConnectedToServer()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string logDir = Path.Combine(localAppData, "FiveM", "FiveM.app", "logs");

            if (!Directory.Exists(logDir))
            {
                // Fallback check from process location
                var procs = Process.GetProcessesByName("FiveM");
                if (procs == null || procs.Length == 0)
                {
                    procs = Process.GetProcessesByName("FiveM_b3407_GTAProcess");
                }

                if (procs != null && procs.Length > 0)
                {
                    try
                    {
                        string? exe = procs[0].MainModule?.FileName;
                        if (!string.IsNullOrEmpty(exe))
                        {
                            var cur = new DirectoryInfo(Path.GetDirectoryName(exe) ?? "");
                            while (cur != null)
                            {
                                string cand = Path.Combine(cur.FullName, "logs");
                                if (Directory.Exists(cand)) { logDir = cand; break; }
                                cand = Path.Combine(cur.FullName, "FiveM.app", "logs");
                                if (Directory.Exists(cand)) { logDir = cand; break; }
                                cur = cur.Parent;
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        foreach (var p in procs) p.Dispose();
                    }
                }
            }

            if (string.IsNullOrEmpty(logDir) || !Directory.Exists(logDir))
            {
                return false;
            }

            var dirInfo = new DirectoryInfo(logDir);
            var latestLog = dirInfo.GetFiles("CitizenFX_log_*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latestLog == null) return false;

            // Ensure the log file is recent (within 4 hours)
            if ((DateTime.UtcNow - latestLog.LastWriteTimeUtc).TotalHours > 4)
            {
                return false;
            }

            // Read the file with FileShare.ReadWrite so FiveM's logger is never blocked
            using var fs = new FileStream(latestLog.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            // Read only the tail (last 64 KB) of the log to prevent massive memory allocations every second
            const int tailBytes = 65536;
            if (fs.Length > tailBytes)
            {
                fs.Seek(-tailBytes, SeekOrigin.End);
            }
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string content = reader.ReadToEnd();

            // Check for connection milestones
            int connectIdx = -1;
            string[] connectKeywords = { "connectOK", "Network connected", "INIT_SESSION" };
            foreach (var kw in connectKeywords)
            {
                int idx = content.LastIndexOf(kw, StringComparison.OrdinalIgnoreCase);
                if (idx > connectIdx) connectIdx = idx;
            }

            if (connectIdx < 0)
            {
                return false; // Sitting in main menu
            }

            // Check if there is an explicit disconnect after connecting
            string[] disconnectKeywords = { 
                "Connection dropped", 
                "connection timed out", 
                "Server->Client connection timed out", 
                "Disconnected from server",
                "Waiting for game to shutdown"
            };

            int disconnectIdx = -1;
            foreach (var kw in disconnectKeywords)
            {
                int idx = content.LastIndexOf(kw, StringComparison.OrdinalIgnoreCase);
                if (idx > disconnectIdx) disconnectIdx = idx;
            }

            if (disconnectIdx > connectIdx)
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
