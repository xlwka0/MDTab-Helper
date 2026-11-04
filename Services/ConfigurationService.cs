using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MDHelper.Models;

namespace MDHelper.Services;

public class ConfigurationService
{
    private readonly string _configPath;
    private readonly string _backupPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _lock = new();

    public ConfigurationService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appData, "MDTab-Helper");
        Directory.CreateDirectory(appFolder);
        _configPath = Path.Combine(appFolder, "config.json");
        _backupPath = Path.Combine(appFolder, "config.json.bak");
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
    }

    public static void EnsureDefaultAssets()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string assetsFolder = Path.Combine(appData, "MDTab-Helper", "Assets");
            Directory.CreateDirectory(assetsFolder);

            string targetFile = Path.Combine(assetsFolder, "coduri_radio.png");
            if (!File.Exists(targetFile) || new FileInfo(targetFile).Length == 0)
            {
                // First check if it exists in base directory (e.g. dev build or unpacked)
                string localFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "coduri_radio.png");
                if (File.Exists(localFile))
                {
                    File.Copy(localFile, targetFile, true);
                }
                else
                {
                    Stream? stream = null;
                    try
                    {
                        var sri = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/MDTab-Helper;component/Assets/coduri_radio.png", UriKind.Absolute));
                        if (sri != null) stream = sri.Stream;
                    }
                    catch { }

                    if (stream == null)
                    {
                        try
                        {
                            if (System.Windows.Application.ResourceAssembly == null)
                            {
                                System.Windows.Application.ResourceAssembly = typeof(ConfigurationService).Assembly;
                            }
                            var sri = System.Windows.Application.GetResourceStream(new Uri("/Assets/coduri_radio.png", UriKind.Relative));
                            if (sri != null) stream = sri.Stream;
                        }
                        catch { }
                    }

                    if (stream != null)
                    {
                        using (stream)
                        using (var outStream = File.Create(targetFile))
                        {
                            stream.CopyTo(outStream);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to extract default assets: {ex.Message}");
        }
    }

    public AppConfiguration LoadConfiguration()
    {
        EnsureDefaultAssets();

        lock (_lock)
        {
            AppConfiguration? config = null;

            // 1. Try loading primary configuration
            if (File.Exists(_configPath))
            {
                try
                {
                    var json = File.ReadAllText(_configPath, System.Text.Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        config = JsonSerializer.Deserialize<AppConfiguration>(json, _jsonOptions);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading primary config: {ex.Message}");
                    try
                    {
                        // Preserve corrupted file for forensics before anything overwrites it
                        string errPath = _configPath + $".corrupt_{DateTime.Now:yyyyMMdd_HHmmss}";
                        File.Copy(_configPath, errPath, overwrite: true);
                    }
                    catch { }
                }
            }

            // 2. If primary failed or was invalid, attempt recovery from backup
            if ((config == null || config.Keybind == null) && File.Exists(_backupPath))
            {
                try
                {
                    var bakJson = File.ReadAllText(_backupPath, System.Text.Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(bakJson))
                    {
                        config = JsonSerializer.Deserialize<AppConfiguration>(bakJson, _jsonOptions);
                        if (config != null)
                        {
                            System.Diagnostics.Debug.WriteLine("Successfully recovered configuration from backup file.");
                            // Restore primary file immediately from healthy backup
                            SaveConfiguration(config);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error recovering from backup config: {ex.Message}");
                }
            }

            config ??= new AppConfiguration();

            // If config has no images at all (brand new setup or empty images list), add default radio codes overlay
            if (config.Images == null || config.Images.Count == 0)
            {
                config.Images = new List<OverlayImage> { AppConfiguration.CreateDefaultRadioCodesOverlay() };
            }

            // Ensure default keybinds list only has 1 default if user hasn't customized the original 2 placeholders
            if (config.Keybind?.Entries != null && config.Keybind.Entries.Count == 2
                && config.Keybind.Entries[0].Name == "Keybind 1" && config.Keybind.Entries[0].Script == "/me exemplu 1"
                && config.Keybind.Entries[1].Name == "Keybind 2" && config.Keybind.Entries[1].Script == "/me exemplu 2")
            {
                config.Keybind.Entries.RemoveAt(1);
            }

            return config;
        }
    }

    public void SaveConfiguration(AppConfiguration config)
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, _jsonOptions);

                // 1. Maintain a healthy backup if current file exists and is non-empty
                if (File.Exists(_configPath))
                {
                    try
                    {
                        var info = new FileInfo(_configPath);
                        if (info.Length > 20)
                        {
                            File.Copy(_configPath, _backupPath, overwrite: true);
                        }
                    }
                    catch { }
                }

                // 2. Atomic write using temporary file to prevent zero-byte corruption
                string tempPath = _configPath + ".tmp";
                File.WriteAllText(tempPath, json, System.Text.Encoding.UTF8);

                // 3. Replace atomically
                File.Move(tempPath, _configPath, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save configuration: {ex.Message}");
            }
        }
    }

    public void ExportConfiguration(AppConfiguration config, string path)
    {
        var json = JsonSerializer.Serialize(config, _jsonOptions);
        File.WriteAllText(path, json, System.Text.Encoding.UTF8);
    }

    public AppConfiguration? ImportConfiguration(string path)
    {
        try
        {
            EnsureDefaultAssets();
            var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            return JsonSerializer.Deserialize<AppConfiguration>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
