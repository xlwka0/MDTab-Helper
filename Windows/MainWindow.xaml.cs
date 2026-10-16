using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using MDHelper.Models;
using MDHelper.Services;
using Microsoft.Win32;

namespace MDHelper.Windows;

public partial class MainWindow : Window
{
    private readonly ConfigurationService _configService;
    private AppConfiguration _config = new();
    private ObservableCollection<OverlayImage> _images = new();
    private List<OverlayWindow> _overlayWindows = new();
    private bool _overlaysVisible = false;
    private KeybindOrchestrator? _keybind;
    private TabletOverlayWindow? _tabletWindow;
    private CopilotService? _copilot;
    private TrayIconService? _trayIcon;
    private bool _isExiting = false;

    public MainWindow()
    {
        InitializeComponent();
        _configService = new ConfigurationService();
        
        ImagesDataGrid.ItemsSource = _images;
        LoadConfiguration();
        UpdateStatus();

        StartupService.SyncStartupWithConfig(_config.Keybind.StartWithWindows);

        // Force handle creation so hotkeys register silently in background without showing the window
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        helper.EnsureHandle();

        _keybind = new KeybindOrchestrator(_config.Keybind);
        _keybind.Initialize(this);
        _keybind.OverlayToggleRequested += Keybind_OverlayToggleRequested;
        _keybind.TabletToggleRequested += Keybind_TabletToggleRequested;
        _tabletWindow = new TabletOverlayWindow(_config.Keybind, _config, _images, SaveConfiguration, RefreshOverlays, ToggleAllOverlays, RefreshKeybinds);
        _tabletWindow.SettingsSaved += TabletWindow_SettingsSaved;

        _copilot = new CopilotService();
        ApplyCopilotSettings();
        _copilot.Start();

        InitializeTrayIcon();
    }

    private void LoadConfiguration()
    {
        _config = _configService.LoadConfiguration();
        
        UpdateMuteButtonDisplay();
        
        foreach (var image in _config.Images)
        {
            if (File.Exists(image.GetAbsoluteFilePath()))
            {
                _images.Add(image);
            }
        }
        
        UpdateStatus();
    }

    private System.Windows.Threading.DispatcherTimer? _saveDebounceTimer;

    private void SaveConfiguration()
    {
        _config.Images = _images.ToList();

        if (_saveDebounceTimer == null)
        {
            _saveDebounceTimer = new System.Windows.Threading.DispatcherTimer
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
        UpdateStatus();
    }

    private void RefreshKeybinds()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        _keybind?.Apply(this, _config.Keybind);
        ApplyCopilotSettings();
        UpdateMuteButtonDisplay();
        SaveConfiguration();
    }

    private void AddImageBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Image",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var filePath in dialog.FileNames)
            {
                var image = new OverlayImage
                {
                    FilePath = GetRelativePath(filePath),
                    X = 100 + (_images.Count * 30),
                    Y = 100 + (_images.Count * 30)
                };
                
                _images.Add(image);
            }
            
            SaveConfiguration();
            UpdateStatus();
            
            // If overlays are already visible, show the new ones
            if (_overlaysVisible)
            {
                ShowOverlays();
            }
        }
    }

    private void RemoveImageBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ImagesDataGrid.SelectedItem is not OverlayImage selectedImage)
        {
            MessageBox.Show("Please select an image to remove.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Close the overlay window if open
        var window = _overlayWindows.FirstOrDefault(w => w.ImageData == selectedImage);
        if (window != null)
        {
            window.Close();
            _overlayWindows.Remove(window);
        }

        _images.Remove(selectedImage);
        SaveConfiguration();
        UpdateStatus();
        ClearPropertiesPanel();
    }

    private void Keybind_OverlayToggleRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            ToggleAllOverlays();
        });
    }

    private void Keybind_TabletToggleRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _tabletWindow?.ToggleVisibility();
        });
    }

    private void ToggleAllOverlays()
    {
        if (_overlaysVisible)
        {
            HideOverlays();
            ToggleOverlayBtn.Content = "👁️ Show Overlays";
            _overlaysVisible = false;
            SoundService.PlayOverlayHide();
        }
        else
        {
            if (!_images.Any()) return;
            ShowOverlays();
            ToggleOverlayBtn.Content = "👁️ Hide Overlays";
            _overlaysVisible = true;
            SoundService.PlayOverlayShow();
        }
    }

    private void ToggleOverlayBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_overlaysVisible && !_images.Any())
        {
            MessageBox.Show("No images added yet.\n\nClick '+ Add Image' to add images first.", "No Images", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ToggleAllOverlays();
    }

    private void ShowOverlays()
    {
        // Close existing windows first
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
        UpdateStatus();
        ClearPropertiesPanel();
    }

    private void OnPositionChanged(object? sender, OverlayImage image)
    {
        SaveConfiguration();
        
        // Update UI if this image is selected
        if (ImagesDataGrid.SelectedItem == image)
        {
            UpdatePropertiesPanel(image);
        }
    }

    private void SaveConfigBtn_Click(object sender, RoutedEventArgs e)
    {
        SaveConfiguration();
        StatusText.Text = "Configuration saved";
    }

    private void LoadConfigBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load Configuration",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            var config = _configService.ImportConfiguration(dialog.FileName);
            if (config != null)
            {
                HideOverlays();
                _overlaysVisible = false;
                ToggleOverlayBtn.Content = "Show Overlays";
                
                _images.Clear();
                foreach (var image in config.Images.Where(i => File.Exists(i.GetAbsoluteFilePath())))
                {
                    _images.Add(image);
                }
                
                SaveConfiguration();
                UpdateStatus();
                StatusText.Text = "Configuration loaded";
            }
            else
            {
                MessageBox.Show("Failed to load configuration file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ImportBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            Title = "Import Configuration"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var config = _configService.ImportConfiguration(dialog.FileName);
                _images.Clear();
                if (config?.Images != null)
                {
                    foreach (var image in config.Images)
                    {
                        if (image != null)
                            _images.Add(image);
                    }
                }
                SaveConfiguration();
                UpdateStatus();

                // Recreate tablet overlay with new settings
                if (_tabletWindow != null)
                {
                    _tabletWindow.ForceClose = true;
                    _tabletWindow.Close();
                }
                _tabletWindow = new TabletOverlayWindow(_config.Keybind, _config, _images, SaveConfiguration, RefreshOverlays, ToggleAllOverlays, RefreshKeybinds);
                _tabletWindow.SettingsSaved += TabletWindow_SettingsSaved;

                StatusText.Text = "Configuration imported successfully";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to import configuration: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void KeybindBtn_Click(object sender, RoutedEventArgs e)
    {
        var window = new KeybindWindow(_config.Keybind)
        {
            Owner = this
        };

        if (window.ShowDialog() == true)
        {
            _config.Keybind = window.Settings;
            SaveConfiguration();
            ApplyCopilotSettings();
            _keybind?.Apply(this, _config.Keybind);

            // Re-create tablet window to pick up new settings
            if (_tabletWindow != null)
            {
                _tabletWindow.ForceClose = true;
                _tabletWindow.Close();
            }
            _tabletWindow = new TabletOverlayWindow(_config.Keybind, _config, _images, SaveConfiguration, RefreshOverlays, ToggleAllOverlays, RefreshKeybinds);
            _tabletWindow.SettingsSaved += TabletWindow_SettingsSaved;

            StatusText.Text = "Keybind settings saved";
        }
    }

    private void WindowToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        var window = new WindowToggleSettingsWindow(_config.Keybind)
        {
            Owner = this
        };

        if (window.ShowDialog() == true)
        {
            _config.Keybind = window.Settings;
            SaveConfiguration();
            ApplyCopilotSettings();
            _keybind?.Apply(this, _config.Keybind);

            // Re-create tablet window to pick up new settings
            if (_tabletWindow != null)
            {
                _tabletWindow.ForceClose = true;
                _tabletWindow.Close();
            }
            _tabletWindow = new TabletOverlayWindow(_config.Keybind, _config, _images, SaveConfiguration, RefreshOverlays, ToggleAllOverlays, RefreshKeybinds);
            _tabletWindow.SettingsSaved += TabletWindow_SettingsSaved;

            StatusText.Text = "Display control settings saved";
        }
    }

    private void ExportBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_images.Any())
        {
            MessageBox.Show("No images to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = "json",
            Title = "Export Configuration"
        };

        if (dialog.ShowDialog() == true)
        {
            var config = new AppConfiguration
            {
                Images = _images.ToList()
            };
            _configService.ExportConfiguration(config, dialog.FileName);
            StatusText.Text = "Configuration exported";
        }
    }

    private void ImagesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImagesDataGrid.SelectedItem is OverlayImage image)
        {
            PopulatePropertiesPanel(image);
        }
        else
        {
            ClearPropertiesPanel();
        }
    }

    private void PopulatePropertiesPanel(OverlayImage image)
    {
        StatusText.Text = $"Properties enabled for: {image.Name}";
        
        PropertiesPanel.IsEnabled = true;
        NoSelectionText.Visibility = Visibility.Collapsed;
        
        OpacitySlider.Value = image.Opacity;
        OpacityValueText.Text = $"{(image.Opacity * 100):F0}%";
        
        PosXTextBox.Text = image.X.ToString("F0");
        PosYTextBox.Text = image.Y.ToString("F0");
        WidthTextBox.Text = image.Width.ToString("F0");
        HeightTextBox.Text = image.Height.ToString("F0");
        
        MaintainAspectCheckBox.IsChecked = image.MaintainAspectRatio;
        LockedCheckBox.IsChecked = image.IsLocked;
    }

    private void ClearPropertiesPanel()
    {
        PropertiesPanel.IsEnabled = false;
        NoSelectionText.Visibility = Visibility.Visible;
    }

    private void UpdatePropertiesPanel(OverlayImage image)
    {
        if (ImagesDataGrid.SelectedItem == image)
        {
            PosXTextBox.Text = image.X.ToString("F0");
            PosYTextBox.Text = image.Y.ToString("F0");
            WidthTextBox.Text = image.Width.ToString("F0");
            HeightTextBox.Text = image.Height.ToString("F0");
        }
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ImagesDataGrid.SelectedItem is OverlayImage image)
        {
            image.Opacity = e.NewValue;
            OpacityValueText.Text = $"{(e.NewValue * 100):F0}%";
            SaveConfiguration();
        }
    }

    private void PositionTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ImagesDataGrid.SelectedItem is not OverlayImage image) return;
        
        if (double.TryParse(PosXTextBox.Text, out var x))
            image.X = x;
        if (double.TryParse(PosYTextBox.Text, out var y))
            image.Y = y;
        
        SaveConfiguration();
    }

    private void SizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ImagesDataGrid.SelectedItem is not OverlayImage image) return;
        
        if (double.TryParse(WidthTextBox.Text, out var width))
            image.Width = width;
        if (double.TryParse(HeightTextBox.Text, out var height))
            image.Height = height;
        
        SaveConfiguration();
    }

    private void MaintainAspectCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (ImagesDataGrid.SelectedItem is OverlayImage image)
        {
            image.MaintainAspectRatio = true;
            SaveConfiguration();
        }
    }

    private void MaintainAspectCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (ImagesDataGrid.SelectedItem is OverlayImage image)
        {
            image.MaintainAspectRatio = false;
            SaveConfiguration();
        }
    }

    private void LockedCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (ImagesDataGrid.SelectedItem is OverlayImage image)
        {
            image.IsLocked = true;
            SaveConfiguration();
        }
    }

    private void LockedCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (ImagesDataGrid.SelectedItem is OverlayImage image)
        {
            image.IsLocked = false;
            SaveConfiguration();
        }
    }

    private void ResetPositionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ImagesDataGrid.SelectedItem is not OverlayImage image) return;
        
        image.X = 100;
        image.Y = 100;
        SaveConfiguration();
        PopulatePropertiesPanel(image);
    }

    private void BringToFrontBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ImagesDataGrid.SelectedItem is not OverlayImage image) return;
        
        var window = _overlayWindows.FirstOrDefault(w => w.ImageData == image);
        window?.Activate();
        window?.Focus();
    }

    private void UpdateStatus()
    {
        StatusText.Text = $"Ready - {_images.Count} image(s) loaded | Overlays: {(_overlaysVisible ? "Visible" : "Hidden")}";
    }

    public static string GetRelativePath(string fullPath)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        if (!baseDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            baseDir += Path.DirectorySeparatorChar;
        }

        if (fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath.Substring(baseDir.Length);
        }
        return fullPath;
    }

    private void InitializeTrayIcon()
    {
        try
        {
            _trayIcon = new TrayIconService();

            // Load icon from file or default application icon
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "1.ico");

            var contextMenu = new ContextMenu();
            
            var openItem = new MenuItem { Header = "🖥️  Open MDTab-Helper" };
            openItem.Click += (s, e) => ShowMainWindow();

            var tabletItem = new MenuItem { Header = "📱  Toggle Tablet" };
            tabletItem.Click += (s, e) => _tabletWindow?.ToggleVisibility();

            var overlayItem = new MenuItem { Header = "👁️  Toggle Overlays" };
            overlayItem.Click += (s, e) => ToggleAllOverlays();

            var soundsItem = new MenuItem { Header = "🔊  Toggle Sounds" };
            soundsItem.Click += (s, e) => ToggleSoundsFromTray();

            var exitItem = new MenuItem { Header = "❌  Exit" };
            exitItem.Click += (s, e) => ExitApplication();

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(tabletItem);
            contextMenu.Items.Add(overlayItem);
            contextMenu.Items.Add(soundsItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenu = contextMenu;
            _trayIcon.LeftClick = ToggleMainWindowVisibility;
            _trayIcon.Initialize(this, "MDTab-Helper", iconPath);
        }
        catch
        {
            // Ignore tray icon initialization error
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;

    public void ShowMainWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Focus();

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
        }
    }

    private void ToggleMainWindowVisibility()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            Hide();
        }
        else
        {
            ShowMainWindow();
        }
    }

    private void ToggleSoundsFromTray()
    {
        _config.Keybind.SoundsEnabled = !_config.Keybind.SoundsEnabled;
        SoundService.Enabled = _config.Keybind.SoundsEnabled;
        if (_keybind != null) _keybind.Apply(this, _config.Keybind);
        SaveConfiguration();
        UpdateMuteButtonDisplay();
    }

    public void ExitApplicationDirectly()
    {
        _isExiting = true;
        FlushConfigurationToDisk();
        Close();
    }

    public void PrepareForExit()
    {
        _isExiting = true;
        FlushConfigurationToDisk();
    }

    private void ExitApplication()
    {
        ExitApplicationDirectly();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
        base.OnStateChanged(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExiting)
        {
            // Minimize/hide to system tray instead of closing completely
            e.Cancel = true;
            Hide();
            return;
        }

        _trayIcon?.Dispose();
        _trayIcon = null;

        HideOverlays();
        _keybind?.Dispose();
        _copilot?.Dispose();

        if (_tabletWindow != null)
        {
            _tabletWindow.ForceClose = true;
            _tabletWindow.Close();
        }

        FlushConfigurationToDisk();
        base.OnClosing(e);
    }

    private void TabletToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        _tabletWindow?.ToggleVisibility();
    }

    private void TabletWindow_SettingsSaved(object? sender, EventArgs e)
    {
        ApplyCopilotSettings();
        SaveConfiguration();
    }

    private void ApplyCopilotSettings()
    {
        if (_copilot != null)
        {
            _copilot.Enabled = _config.Keybind.CopilotEnabled;
            _copilot.CopilotKey = _config.Keybind.CopilotVirtualKey;
            _copilot.TriggerKey = _config.Keybind.CopilotTriggerVirtualKey;
            _copilot.TriggerModifiers = _config.Keybind.CopilotTriggerModifiers;
        }
    }

    private void UpdateMuteButtonDisplay()
    {
        if (MuteBtn != null)
        {
            if (_config.Keybind.SoundsEnabled)
            {
                MuteBtn.Content = "🔊 Sounds: Enabled";
                MuteBtn.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ff2d55"));
            }
            else
            {
                MuteBtn.Content = "🔇 Sounds: Muted";
                MuteBtn.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#374151")); // Dark Gray
            }
        }
    }

    private void MuteBtn_Click(object sender, RoutedEventArgs e)
    {
        _config.Keybind.SoundsEnabled = !_config.Keybind.SoundsEnabled;
        SoundService.Enabled = _config.Keybind.SoundsEnabled;
        
        if (_keybind != null)
        {
            _keybind.Apply(this, _config.Keybind);
        }

        SaveConfiguration();
        UpdateMuteButtonDisplay();

        if (SoundService.Enabled)
        {
            SoundService.PlayKeybindStart();
        }
    }
}
