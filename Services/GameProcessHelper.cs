using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MDHelper.Services;

/// <summary>
/// Helper to detect whether the target game (GTA V / RAGE:MP / FiveM) is running and/or active foreground.
/// </summary>
public static class GameProcessHelper
{
    private static readonly string[] GameProcessPrefixes = new[]
    {
        "gta5",
        "ragemp",
        "rage-mp",
        "playgtav",
        "fivem"
    };

    private static readonly string[] GameWindowKeywords = new[]
    {
        "Grand Theft Auto",
        "RAGE Multiplayer",
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
    /// Checks whether the game window is currently the active foreground window.
    /// </summary>
    public static bool IsGameForeground()
    {
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
    /// Checks whether the game process is running in Windows.
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
        // Fast path: check common exact names
        string[] fastNames = { "GTA5", "gta5", "ragemp_v", "ragemp", "PlayGTAV", "FiveM" };
        foreach (var name in fastNames)
        {
            try
            {
                var procs = Process.GetProcessesByName(name);
                if (procs != null && procs.Length > 0)
                {
                    foreach (var p in procs) p.Dispose();
                    return true;
                }
            }
            catch { }
        }

        // Full search if not found by exact common names
        try
        {
            var allProcs = Process.GetProcesses();
            foreach (var p in allProcs)
            {
                try
                {
                    string pName = p.ProcessName;
                    foreach (var prefix in GameProcessPrefixes)
                    {
                        if (pName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch { }

        return false;
    }
}
