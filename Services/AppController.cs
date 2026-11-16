using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using MDHelper.Models;
using MDHelper.Windows;

namespace MDHelper.Services;

public sealed class AppController : IDisposable
{
    private readonly ConfigurationService _configService = new();
    private AppConfiguration _config = new();
    private readonly ObservableCollection<OverlayImage> _images = new();
    private readonly List<OverlayWindow> _overlayWindows = new();
    private bool _overlaysVisible = false;

    private HwndSource? _hostSource;
    private KeybindOrchestrator? _keybind;
    private TabletOverlayWindow? _tabletWindow;
    private TrayIconService? _trayIcon;
    private TelemetryService? _telemetry;
    private DispatcherTimer? _saveDebounceTimer;
    private bool _disposed = false;

    public TabletOverlayWindow? TabletWindow => _tabletWindow;

    public void Start()
    {
        LoadConfiguration();
        StartupService.SyncStartupWithConfig(_config.Keybind.StartWithWindows);

        var parameters = new HwndSourceParameters("MDTabHelperHost")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0x00000080 // WS_EX_TOOLWINDOW
        };
        _hostSource = new HwndSource(parameters);

        _keybind = new KeybindOrchestrator(_config.Keybind);
        _keybind.OverlayToggleRequested += Keybind_OverlayToggleRequested;
        _keybind.TabletToggleRequested += Keybind_TabletToggleRequested;
        _keybind.Initialize(_hostSource);

        _tabletWindow = new TabletOverlayWindow(
            _config.Keybind,
            _config,
            _images,
            SaveConfiguration,
            RefreshOverlays,
            ToggleAllOverlays,
            RefreshKeybinds);
        _tabletWindow.SettingsSaved += TabletWindow_SettingsSaved;

        if (Application.Current != null)
        {
            Application.Current.MainWindow = _tabletWindow;
        }

        _telemetry = new TelemetryService(_config.Keybind, SaveConfiguration);
        _telemetry.Start();

        InitializeTrayIcon();
    }

    private void LoadConfiguration()
    {
        _config = _configService.LoadConfiguration();
        _images.Clear();

        foreach (var image in _config.Images)
        {
            if (image != null && File.Exists(image.GetAbsoluteFilePath()))
            {
                _images.Add(image);
            }
        }
    }

    public void SaveConfiguration()
    {
        _config.Images = _images.ToList();

        if (_saveDebounceTimer == null)
        {
            _saveDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _saveDebounceTimer.Tick += (s, e) =>
            {
                _saveDebounceTimer.Stop();
                FlushConfigurationToDisk();
            };
        }

        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }

    public void FlushConfigurationToDisk()
    {
        _saveDebounceTimer?.Stop();
        _config.Images = _images.ToList();
        _configService.SaveConfiguration(_config);
    }

    private void RefreshOverlays()
    {
        if (_overlaysVisible)
        {
            ShowOverlays();
        }
    }

    private void RefreshKeybinds()
    {
        _keybind?.Apply(_config.Keybind);
        SaveConfiguration();
    }

    private void Keybind_OverlayToggleRequested(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(ToggleAllOverlays);
    }

    private void Keybind_TabletToggleRequested(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() => _tabletWindow?.ToggleVisibility());
    }

    private void ToggleAllOverlays()
    {
        if (_overlaysVisible)
        {
            HideOverlays();
            _overlaysVisible = false;
            SoundService.PlayOverlayHide();
        }
        else
        {
            if (!_images.Any()) return;
            ShowOverlays();
            _overlaysVisible = true;
            SoundService.PlayOverlayShow();
        }
    }

    private void ShowOverlays()
    {
        HideOverlays();
        _overlayWindows.Clear();

        foreach (var image in _images.Where(i => i.IsVisible))
        {
            var window = new OverlayWindow(image, _configService);
            window.ImageRemoved += OnImageRemoved;
            window.PositionChanged += OnPositionChanged;
            window.Show();
            _overlayWindows.Add(window);
        }
    }

    private void HideOverlays()
    {
        foreach (var window in _overlayWindows)
        {
            window.Close();
        }
        _overlayWindows.Clear();
    }

    private void OnImageRemoved(object? sender, OverlayImage image)
    {
        var window = _overlayWindows.FirstOrDefault(w => w.ImageData == image);
        if (window != null)
        {
            _overlayWindows.Remove(window);
        }

        _images.Remove(image);
        SaveConfiguration();
    }

    private void OnPositionChanged(object? sender, OverlayImage image)
    {
        SaveConfiguration();
        _tabletWindow?.OnOverlayImagePositionChanged(image);
    }

    private void TabletWindow_SettingsSaved(object? sender, EventArgs e)
    {
        SaveConfiguration();
    }

    private void InitializeTrayIcon()
    {
        if (_hostSource == null) return;

        try
        {
            _trayIcon = new TrayIconService();
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "1.ico");

            var contextMenu = new ContextMenu();

            var tabletItem = new MenuItem { Header = "📱  Toggle Tablet" };
            tabletItem.Click += (s, e) => _tabletWindow?.ToggleVisibility();

            var overlayItem = new MenuItem { Header = "👁️  Toggle Overlays" };
            overlayItem.Click += (s, e) => ToggleAllOverlays();

            var soundsItem = new MenuItem { Header = "🔊  Toggle Sounds" };
            soundsItem.Click += (s, e) => ToggleSoundsFromTray();

            var exitItem = new MenuItem { Header = "❌  Exit" };
            exitItem.Click += (s, e) => (Application.Current as App)?.ExitApplication();

            contextMenu.Items.Add(tabletItem);
            contextMenu.Items.Add(overlayItem);
            contextMenu.Items.Add(soundsItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenu = contextMenu;
            _trayIcon.LeftClick = () => _tabletWindow?.ToggleVisibility();
            _trayIcon.Initialize(_hostSource, "MDTab-Helper", iconPath);
        }
        catch
        {
            // Ignore tray icon initialization error
        }
    }

    private void ToggleSoundsFromTray()
    {
        _config.Keybind.SoundsEnabled = !_config.Keybind.SoundsEnabled;
        SoundService.Enabled = _config.Keybind.SoundsEnabled;
        _keybind?.Apply(_config.Keybind);
        _tabletWindow?.ReloadSettingsFromConfig(_config.Keybind);
        SaveConfiguration();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _saveDebounceTimer?.Stop();

        _trayIcon?.Dispose();
        _trayIcon = null;

        HideOverlays();

        _keybind?.Dispose();
        _keybind = null;

        _telemetry?.Dispose();
        _telemetry = null;

        if (_tabletWindow != null)
        {
            _tabletWindow.ForceClose = true;
            _tabletWindow.Close();
            _tabletWindow = null;
        }

        FlushConfigurationToDisk();

        _hostSource?.Dispose();
        _hostSource = null;
    }
}
