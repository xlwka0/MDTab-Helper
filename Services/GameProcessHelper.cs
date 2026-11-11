using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MDHelper.Services;

/// <summary>
/// Helper to detect whether the target game (GTA V / FiveM / RAGE:MP) is running and active foreground.
/// </summary>
public static class GameProcessHelper
{
    private static readonly string[] GameProcessPrefixes = new[]
    {
        "gta5",
        "fivem",
        "ragemp",
        "rage-mp",
        "playgtav"
    };

    private static readonly string[] GameWindowKeywords = new[]
    {
        "Grand Theft Auto",
        "FiveM",
        "GTA5",
        "GTA V",
        "GTA:V",
        "RAGE Multiplayer"
    };

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    private static bool _cachedIsRunning = false;
    private static DateTime _lastRunningCheck = DateTime.MinValue;
    private static readonly object _runningLock = new();

    /// <summary>
    /// Checks whether the game window is currently the active foreground window.
    /// Fast-path executed in <0.01 ms with zero disk access to guarantee keybind responsiveness.
    /// </summary>
    public static bool IsGameForeground()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        // 1. Process name check of foreground window PID (<0.01 ms)
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

        // 3. Window class check as fallback (GTA V and FiveM native window class is always "grcWindow")
        try
        {
            var sbClass = new StringBuilder(256);
            if (GetClassName(hwnd, sbClass, sbClass.Capacity) > 0)
            {
                string className = sbClass.ToString();
                if (className.Equals("grcWindow", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
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
        // 1. If foreground window is the game, it is definitely running
        if (IsGameForeground()) return true;

        // 2. Fast direct process check for common game processes
        string[] fastNames = { 
            "GTA5", "gta5", 
            "FiveM_b3407_GTAProcess", "FiveM_GTAProcess", "FiveM_b3258_GTAProcess", 
            "FiveM_b2699_GTAProcess", "FiveM_b2802_GTAProcess", "FiveM_b2944_GTAProcess", 
            "FiveM_b3095_GTAProcess", "FiveM", 
            "ragemp_v", "ragemp", "PlayGTAV" 
        };
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

        // 3. Prefix match across all running processes if non-standard build name
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
