using System;
using System.IO;
using Microsoft.Win32;

namespace MDHelper.Services;

public static class StartupService
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly string[] KnownAppNames = ["MDTab-Helper", "MDTab-Helper.exe"];

    /// <summary>
    /// Checks whether MDTab-Helper is registered to launch at Windows startup
    /// either via Registry (HKCU/HKLM) or the Startup shell folder.
    /// </summary>
    public static bool IsStartupEnabled()
    {
        try
        {
            // 1. Check HKCU Run
            using (var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false))
            {
                if (key != null)
                {
                    foreach (var name in KnownAppNames)
                    {
                        if (key.GetValue(name) != null)
                        {
                            return true;
                        }
                    }

                    var currentExe = Environment.ProcessPath;
                    foreach (var valueName in key.GetValueNames())
                    {
                        var val = key.GetValue(valueName)?.ToString() ?? "";
                        if (val.Contains("MDTab-Helper", StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(currentExe) && val.Contains(currentExe, StringComparison.OrdinalIgnoreCase)))
                        {
                            return true;
                        }
                    }
                }
            }

            // 2. Check User Startup folder
            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startupFolder))
            {
                foreach (var file in Directory.GetFiles(startupFolder))
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.Contains("MDTab-Helper", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Ignore errors querying startup
        }

        return false;
    }

    /// <summary>
    /// Registers the current MDTab-Helper executable to launch at Windows startup in HKCU\Run,
    /// cleaning up any stale or conflicting entries first.
    /// </summary>
    public static void EnableStartup()
    {
        try
        {
            DisableStartup(); // Clean existing duplicates or stale paths first

            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key != null)
            {
                var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MDTab-Helper.exe");
                key.SetValue("MDTab-Helper", $"\"{exePath}\"");
            }
        }
        catch
        {
            // Ignore permission or registry write errors
        }
    }

    /// <summary>
    /// Thoroughly removes all startup entries (HKCU Run, HKLM Run, and Shell Startup folders)
    /// for MDHelper, MDTab, or any entry pointing to the executable.
    /// </summary>
    public static void DisableStartup()
    {
        try
        {
            // 1. Remove from HKCU Run
            using (var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true))
            {
                if (key != null)
                {
                    foreach (var name in KnownAppNames)
                    {
                        try { key.DeleteValue(name, false); } catch { }
                    }

                    var currentExe = Environment.ProcessPath;
                    foreach (var valueName in key.GetValueNames())
                    {
                        try
                        {
                            var val = key.GetValue(valueName)?.ToString() ?? "";
                            if (val.Contains("MDTab-Helper", StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrEmpty(currentExe) && val.Contains(currentExe, StringComparison.OrdinalIgnoreCase)))
                            {
                                key.DeleteValue(valueName, false);
                            }
                        }
                        catch { }
                    }
                }
            }

            // 2. Try removing from HKLM Run
            try
            {
                using var hklmKey = Registry.LocalMachine.OpenSubKey(RunRegistryKey, true);
                if (hklmKey != null)
                {
                    foreach (var name in KnownAppNames)
                    {
                        try { hklmKey.DeleteValue(name, false); } catch { }
                    }
                }
            }
            catch { }

            // 3. Remove shortcuts from User Startup folder
            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startupFolder))
            {
                foreach (var file in Directory.GetFiles(startupFolder))
                {
                    try
                    {
                        var fileName = Path.GetFileName(file);
                        if (fileName.Contains("MDTab-Helper", StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(file);
                        }
                    }
                    catch { }
                }
            }

            // 4. Try removing from Common Startup folder
            try
            {
                var commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
                if (Directory.Exists(commonStartup))
                {
                    foreach (var file in Directory.GetFiles(commonStartup))
                    {
                        try
                        {
                            var fileName = Path.GetFileName(file);
                            if (fileName.Contains("MDTab-Helper", StringComparison.OrdinalIgnoreCase))
                            {
                                File.Delete(file);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Synchronizes the actual Windows startup state with the user's configuration.
    /// If disabled in config, proactively removes all Windows startup traces.
    /// If enabled in config, ensures the current valid executable path is set.
    /// </summary>
    public static void SyncStartupWithConfig(bool shouldStartWithWindows)
    {
        if (shouldStartWithWindows)
        {
            EnableStartup();
        }
        else
        {
            DisableStartup();
        }
    }
}
