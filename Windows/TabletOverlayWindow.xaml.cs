using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using MDHelper.Models;
using MDHelper.Services;

namespace MDHelper.Windows;

public partial class TabletOverlayWindow : Window
{
    private readonly KeybindSettings _settings;
    private readonly AppConfiguration _config;
    private readonly ObservableCollection<OverlayImage> _images;
    private readonly Action _saveConfigCallback;
    private readonly Action _refreshOverlaysCallback;
    private readonly Action _toggleOverlaysCallback;
    private readonly Action _refreshKeybindsCallback;
    private readonly ObservableCollection<KeybindViewModel> _tabletKeybinds;
    private readonly DispatcherTimer _clockTimer;
    private bool _isInitialized = false;
    private bool _webViewInitialized = false;
    private bool _isPopulatingKeybindPanel = false;

    // Recording keybind flags
    private KeybindViewModel? _recordingKeybindEntry;
    private bool _isRecordingTabletKeybind = false;
    private bool _isRecordingTabletStopKey = false;
    private bool _isRecordingTabletWtKey = false;
    private bool _isRecordingTabletOtKey = false;
    private bool _isRecordingTabletCopilotKey = false;
    private bool _isRecordingTabletCopilotTrigger = false;

    // On-Duty Medic Dispatch fields
    private readonly OnDutyService _onDutyService = new();
    private DispatcherTimer? _onDutyRefreshTimer;
    private DispatcherTimer? _dutyToastTimer;
    private OnDutyResult? _currentDutyResult;
    private string _dutySearchText = string.Empty;
    private string _dutyRankFilter = "Toți";
    private bool _isSidebarExpanded = false;
    private const double DutySidebarTotalWidth = 310;
    private readonly UpdateService _updateService = new();
    private UpdateInfo? _availableUpdate;
    private CancellationTokenSource? _updateCts;

    public event EventHandler? SettingsSaved;

    public TabletOverlayWindow(
        KeybindSettings settings,
        AppConfiguration config,
        ObservableCollection<OverlayImage> images,
        Action saveConfigCallback,
        Action refreshOverlaysCallback,
        Action toggleOverlaysCallback,
        Action refreshKeybindsCallback)
    {
        InitializeComponent();
        _settings = settings;
        _config = config;
        _images = images;
        _saveConfigCallback = saveConfigCallback;
        _refreshOverlaysCallback = refreshOverlaysCallback;
        _toggleOverlaysCallback = toggleOverlaysCallback;
        _refreshKeybindsCallback = refreshKeybindsCallback;

        // Load size settings
        Width = _settings.TabletWidth;
        Height = _settings.TabletHeight;

        // Apply settings on load
        Opacity = _settings.TabletOpacity;
        OpacitySlider.Value = _settings.TabletOpacity;
        OpacityText.Text = $"{(int)(_settings.TabletOpacity * 100)}%";
        
        ApplyTheme(_settings.TabletTheme);
        UpdateHotkeyBadge();

        // Bind images
        TabletImagesDataGrid.ItemsSource = _images;

        // Bind Keybinds
        _tabletKeybinds = new ObservableCollection<KeybindViewModel>();
        foreach (var entry in _settings.Entries)
        {
            _tabletKeybinds.Add(new KeybindViewModel(entry));
        }
        TabletKeybindsDataGrid.ItemsSource = _tabletKeybinds;

        // Load keybind delays and state
        TabletGlobalKeybindsEnabledCheckBox.IsChecked = _settings.GlobalEnabled;
        TabletGlobalSoundsEnabledCheckBox.IsChecked = _settings.SoundsEnabled;
        TabletDelayMsTextBox.Text = _settings.DelayMs.ToString();
        TabletDelayChatOpenMsTextBox.Text = _settings.DelayAfterChatOpenMs.ToString();
        TabletDelayTextMsTextBox.Text = _settings.DelayAfterTextMs.ToString();
        TabletDelayEnterMsTextBox.Text = _settings.DelayAfterEnterMs.ToString();

        TabletStopHotkeyEnabledCheckBox.IsChecked = _settings.StopHotkeyEnabled;
        UpdateTabletStopKeyDisplay();

        // Load system settings
        TabletWtEnabledCheckBox.IsChecked = _settings.WindowToggle.Enabled;
        TabletWtTitleComboBox.Text = _settings.WindowToggle.WindowTitle;
        TabletWtXTextBox.Text = _settings.WindowToggle.X.ToString();
        TabletWtYTextBox.Text = _settings.WindowToggle.Y.ToString();
        TabletWtWidthTextBox.Text = _settings.WindowToggle.Width.ToString();
        TabletWtHeightTextBox.Text = _settings.WindowToggle.Height.ToString();
        UpdateTabletWtKeyDisplay();

        TabletOtEnabledCheckBox.IsChecked = _settings.OverlayToggleEnabled;
        UpdateTabletOtKeyDisplay();

        bool isStartup = StartupService.IsStartupEnabled();
        _settings.StartWithWindows = isStartup;
        TabletStartWithWindowsCheckBox.IsChecked = isStartup;

        TabletCopilotEnabledCheckBox.IsChecked = _settings.CopilotEnabled;
        UpdateTabletCopilotKeyDisplay();

        // Clock timer
        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += ClockTimer_Tick;
        _clockTimer.Start();
        ClockTimer_Tick(null, EventArgs.Empty);

        LoadWallpaper();
        LockTablet();

        // Close on ESC and listen to size changes
        PreviewKeyDown += TabletOverlayWindow_PreviewKeyDown;
        PreviewKeyUp += TabletOverlayWindow_PreviewKeyUp;
        SizeChanged += TabletOverlayWindow_SizeChanged;
        _isInitialized = true;

        TabletVersionText.Text = $"v{UpdateService.GetCurrentVersion()}";
        SettingsCurrentVersionText.Text = $"v{UpdateService.GetCurrentVersion()}";
        _ = CheckForUpdatesSilentlyAsync();
    }

    private void ClockTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm:ss");

        if (LockscreenClockText != null)
        {
            LockscreenClockText.Text = now.ToString("HH:mm");
        }
        if (LockscreenDateText != null)
        {
            var culture = new System.Globalization.CultureInfo("ro-RO");
            LockscreenDateText.Text = now.ToString("dddd, d MMMM", culture);
        }
    }

    private async void InitializeWebView()
    {
        try
        {
            // Set up a persistent User Data folder so sessions and logins are memorized
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string userFolder = Path.Combine(appData, "MDTab-Helper", "TabletCache");
            Directory.CreateDirectory(userFolder);

            var env = await CoreWebView2Environment.CreateAsync(null, userFolder);
            await TabletWebView.EnsureCoreWebView2Async(env);

            _webViewInitialized = true;
            LoadingIndicator.Visibility = Visibility.Collapsed;

            // Nav to default URL
            TabletWebView.Source = new Uri(_settings.TabletUrl);
            UrlTextBox.Text = _settings.TabletUrl;

            // Wire up webview events
            TabletWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            TabletWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            TabletWebView.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;

            // Apply default zoom
            TabletWebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            ApplyZoom();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Eroare la inițializarea browserului: {ex.Message}\n\nAsigurați-vă că aveți Microsoft Edge WebView2 instalat.", "Eroare inițializare", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        LoadingIndicator.Visibility = Visibility.Visible;
    }

    private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        LoadingIndicator.Visibility = Visibility.Collapsed;
        UpdateNavigationButtons();
    }

    private void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        if (TabletWebView.Source != null)
        {
            UrlTextBox.Text = TabletWebView.Source.ToString();
        }
    }

    private void UpdateNavigationButtons()
    {
        if (_isInitialized && TabletWebView.CoreWebView2 != null)
        {
            BackBtn.IsEnabled = TabletWebView.CoreWebView2.CanGoBack;
            ForwardBtn.IsEnabled = TabletWebView.CoreWebView2.CanGoForward;
        }
    }

    private void UpdateHotkeyBadge()
    {
    }

    private void ApplyTheme(string theme)
    {
        if (theme == "borderless")
        {
            BezelBorder.Background = System.Windows.Media.Brushes.Transparent;
            BezelBorder.BorderThickness = new Thickness(0);
            BezelBorder.CornerRadius = new CornerRadius(0);
            BezelBorder.Padding = new Thickness(0);

            ScreenBorder.Margin = new Thickness(0);
            ScreenBorder.CornerRadius = new CornerRadius(8);

            HardwareNotch.Visibility = Visibility.Collapsed;
            BottomBar.Visibility = Visibility.Collapsed;

            ThemeBtn.Content = "🌐 Ecran";
        }
        else
        {
            // Restore tablet bezel
            BezelBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10141d"));
            BezelBorder.BorderThickness = new Thickness(1);
            BezelBorder.CornerRadius = new CornerRadius(28);

            ScreenBorder.Margin = new Thickness(12, 14, 12, 18);
            ScreenBorder.CornerRadius = new CornerRadius(18);

            HardwareNotch.Visibility = Visibility.Visible;
            BottomBar.Visibility = Visibility.Visible;

            ThemeBtn.Content = "📱 Ramă";
        }
    }

    private void ApplyZoom()
    {
        if (_isInitialized && TabletWebView.CoreWebView2 != null)
        {
            TabletWebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            TabletWebView.ZoomFactor = _settings.TabletZoom;
            ZoomText.Text = $"{(int)(_settings.TabletZoom * 100)}%";
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        HideTablet();
    }

    private void HomeIndicator_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (LockscreenGrid.Visibility == Visibility.Visible)
        {
            HideTablet();
        }
        else if (DesktopGrid.Visibility != Visibility.Visible)
        {
            CloseAllAppsAndGoHome();
        }
        else
        {
            LockTablet();
        }
    }

    private void TabletOverlayWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (LockscreenGrid.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            UnlockTablet();
            return;
        }

        if (_isRecordingTabletKeybind || _isRecordingTabletStopKey || _isRecordingTabletWtKey || 
            _isRecordingTabletOtKey || _isRecordingTabletCopilotKey || _isRecordingTabletCopilotTrigger)
        {
            e.Handled = true;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            
            var isModifierKey = key == Key.LeftCtrl || key == Key.RightCtrl ||
                                key == Key.LeftAlt || key == Key.RightAlt ||
                                key == Key.LeftShift || key == Key.RightShift ||
                                key == Key.LWin || key == Key.RWin;

            // Don't record modifier keys on key down so combinations (e.g. RightCtrl + /) can be registered
            if (isModifierKey)
            {
                return;
            }

            var vk = KeyInterop.VirtualKeyFromKey(key);
            var modifiers = 0u;

            var isRightCtrl = Keyboard.IsKeyDown(Key.RightCtrl);
            var isLeftCtrl = Keyboard.IsKeyDown(Key.LeftCtrl);
            var isRightAlt = Keyboard.IsKeyDown(Key.RightAlt);
            var isLeftAlt = Keyboard.IsKeyDown(Key.LeftAlt);
            var isRightShift = Keyboard.IsKeyDown(Key.RightShift);
            var isLeftShift = Keyboard.IsKeyDown(Key.LeftShift);

            if (isRightCtrl) modifiers |= 0x0012;
            else if (isLeftCtrl) modifiers |= 0x0022;
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= 0x0002;

            if (isRightAlt) modifiers |= 0x0041;
            else if (isLeftAlt) modifiers |= 0x0081;
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= 0x0001;

            if (isRightShift) modifiers |= 0x0104;
            else if (isLeftShift) modifiers |= 0x0204;
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= 0x0004;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= 0x0008;

            if (_isRecordingTabletKeybind && _recordingKeybindEntry != null)
            {
                _recordingKeybindEntry.Entry.HotkeyVirtualKey = vk;
                _recordingKeybindEntry.Entry.HotkeyModifiers = modifiers;
                _recordingKeybindEntry.Refresh();
                _isRecordingTabletKeybind = false;
                TabletRecordKeybindBtn.Content = "🔘 Set Key";
                
                // Show updated key in label
                TabletKeybindKeyText.Text = _recordingKeybindEntry.KeyDisplay;

                _saveConfigCallback?.Invoke();
                _refreshKeybindsCallback?.Invoke();
            }
            else if (_isRecordingTabletStopKey)
            {
                _settings.StopHotkeyVirtualKey = vk;
                _settings.StopHotkeyModifiers = modifiers;
                _isRecordingTabletStopKey = false;
                TabletRecordStopKeyBtn.Content = "🔘 Set Key";
                UpdateTabletStopKeyDisplay();
                _saveConfigCallback?.Invoke();
                _refreshKeybindsCallback?.Invoke();
            }
            else if (_isRecordingTabletWtKey)
            {
                _settings.WindowToggle.HotkeyVirtualKey = vk;
                _settings.WindowToggle.HotkeyModifiers = modifiers;
                _isRecordingTabletWtKey = false;
                TabletRecordWtKeyBtn.Content = "🔴 Set Key";
                UpdateTabletWtKeyDisplay();
                _saveConfigCallback?.Invoke();
                _refreshKeybindsCallback?.Invoke();
            }
            else if (_isRecordingTabletOtKey)
            {
                _settings.OverlayToggleVirtualKey = vk;
                _settings.OverlayToggleModifiers = modifiers;
                _isRecordingTabletOtKey = false;
                TabletRecordOtKeyBtn.Content = "🔴 Set Key";
                UpdateTabletOtKeyDisplay();
                _saveConfigCallback?.Invoke();
                _refreshKeybindsCallback?.Invoke();
            }
            else if (_isRecordingTabletCopilotKey)
            {
                _settings.CopilotVirtualKey = vk;
                _isRecordingTabletCopilotKey = false;
                TabletRecordCopilotKeyBtn.Content = "🔴 Set Key";
                UpdateTabletCopilotKeyDisplay();
                _saveConfigCallback?.Invoke();
                _refreshKeybindsCallback?.Invoke();
            }
            else if (_isRecordingTabletCopilotTrigger)
            {
                _settings.CopilotTriggerVirtualKey = vk;
                _settings.CopilotTriggerModifiers = modifiers;
                _isRecordingTabletCopilotTrigger = false;
                TabletRecordCopilotTriggerBtn.Content = "🔴 Set Key";
                UpdateTabletCopilotKeyDisplay();
                _saveConfigCallback?.Invoke();
                _refreshKeybindsCallback?.Invoke();
            }

            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideTablet();
        }
    }

    private void TabletOverlayWindow_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_isRecordingTabletCopilotTrigger)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var isModifierKey = key == Key.LeftCtrl || key == Key.RightCtrl ||
                                key == Key.LeftAlt || key == Key.RightAlt ||
                                key == Key.LeftShift || key == Key.RightShift ||
                                key == Key.LWin || key == Key.RWin;

            if (isModifierKey)
            {
                // Standalone modifier key released without other keys
                var vk = KeyInterop.VirtualKeyFromKey(key);
                _settings.CopilotTriggerVirtualKey = vk;
                _settings.CopilotTriggerModifiers = 0;

                _isRecordingTabletCopilotTrigger = false;
                TabletRecordCopilotTriggerBtn.Content = "🔴 Set Key";
                UpdateTabletCopilotKeyDisplay();
                _saveConfigCallback?.Invoke();
                _refreshKeybindsCallback?.Invoke();
            }
        }
    }

    public void ToggleVisibility()
    {
        if (Visibility == Visibility.Visible)
        {
            HideTablet();
        }
        else
        {
            ShowTablet();
        }
    }

    public void ShowTablet()
    {
        Visibility = Visibility.Visible;
        Activate();
        Focus();
        if (_isSidebarExpanded && LockscreenGrid != null && LockscreenGrid.Visibility != Visibility.Visible)
        {
            StartDutyAutoRefresh();
            _ = RefreshOnDutyDataAsync(showLoading: false);
        }
        if (_settings.TabletPlaySounds)
        {
            SoundService.PlayOverlayShow();
        }
    }

    public void HideTablet()
    {
        StopDutyAutoRefresh();
        if (_isSidebarExpanded)
        {
            DutySidebarCollapseBtn_Click(this, new RoutedEventArgs());
        }
        Visibility = Visibility.Collapsed;
        if (_settings.TabletPlaySounds)
        {
            SoundService.PlayOverlayHide();
        }
        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    private void TabletOverlayWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isInitialized)
        {
            double effectiveWidth = _isSidebarExpanded ? ActualWidth - DutySidebarTotalWidth : ActualWidth;
            _settings.TabletWidth = (int)Math.Max(800, effectiveWidth);
            _settings.TabletHeight = (int)Math.Max(500, ActualHeight);
        }
    }

    private void BackBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitialized && TabletWebView?.CoreWebView2 != null && TabletWebView.CoreWebView2.CanGoBack)
        {
            TabletWebView.GoBack();
        }
    }

    private void ForwardBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitialized && TabletWebView?.CoreWebView2 != null && TabletWebView.CoreWebView2.CanGoForward)
        {
            TabletWebView.GoForward();
        }
    }

    private void ReloadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitialized && TabletWebView?.CoreWebView2 != null)
        {
            TabletWebView.Reload();
        }
    }

    private void HomeBtn_Click(object sender, RoutedEventArgs e)
    {
        CloseAllAppsAndGoHome();
    }

    private void GoBtn_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(UrlTextBox.Text);
    }

    private void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateTo(UrlTextBox.Text);
        }
    }

    private void NavigateTo(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        try
        {
            TabletWebView.Source = new Uri(url);
        }
        catch
        {
            // Ignore format errors
        }
    }

    private void MdPanelBtn_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo("https://md.b-zone.ro");
    }

    private void GtaPanelBtn_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo("https://gta5.ro");
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settings != null)
        {
            _settings.TabletOpacity = e.NewValue;
            Opacity = e.NewValue;
            if (OpacityText != null)
            {
                OpacityText.Text = $"{(int)(e.NewValue * 100)}%";
            }
        }
    }

    private void ZoomOutBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.TabletZoom = Math.Max(0.5, _settings.TabletZoom - 0.1);
        ApplyZoom();
    }

    private void ZoomInBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.TabletZoom = Math.Min(2.0, _settings.TabletZoom + 0.1);
        ApplyZoom();
    }

    private void ThemeBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.TabletTheme = _settings.TabletTheme == "tablet" ? "borderless" : "tablet";
        ApplyTheme(_settings.TabletTheme);
    }

    public bool ForceClose { get; set; } = false;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (ForceClose)
        {
            _clockTimer.Stop();
            _clockTimer.Tick -= ClockTimer_Tick;
            base.OnClosing(e);
        }
        else
        {
            // Don't close on clicking X of window control - just hide it
            e.Cancel = true;
            HideTablet();
        }
    }



    private void TabletImagesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabletImagesDataGrid.SelectedItem is OverlayImage image)
        {
            PopulateTabletPropertiesPanel(image);
        }
        else
        {
            ClearTabletPropertiesPanel();
        }
    }

    private void PopulateTabletPropertiesPanel(OverlayImage image)
    {
        TabletPropertiesPanel.IsEnabled = true;
        TabletNoSelectionText.Visibility = Visibility.Collapsed;
        
        TabletImageOpacitySlider.Value = image.Opacity;
        TabletImageOpacityValueText.Text = $"{(image.Opacity * 100):F0}%";
        
        TabletPosXTextBox.Text = image.X.ToString("F0");
        TabletPosYTextBox.Text = image.Y.ToString("F0");
        TabletImageWidthTextBox.Text = image.Width.ToString("F0");
        TabletImageHeightTextBox.Text = image.Height.ToString("F0");
        
        TabletMaintainAspectCheckBox.IsChecked = image.MaintainAspectRatio;
        TabletLockedCheckBox.IsChecked = image.IsLocked;
    }

    private void ClearTabletPropertiesPanel()
    {
        TabletPropertiesPanel.IsEnabled = false;
        TabletNoSelectionText.Visibility = Visibility.Visible;
    }

    private void TabletImageOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TabletImagesDataGrid.SelectedItem is OverlayImage image)
        {
            image.Opacity = e.NewValue;
            TabletImageOpacityValueText.Text = $"{(e.NewValue * 100):F0}%";
            _saveConfigCallback?.Invoke();
            _refreshOverlaysCallback?.Invoke();
        }
    }

    private void TabletPositionTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TabletImagesDataGrid.SelectedItem is not OverlayImage image) return;
        
        if (double.TryParse(TabletPosXTextBox.Text, out var x))
            image.X = x;
        if (double.TryParse(TabletPosYTextBox.Text, out var y))
            image.Y = y;
        
        _saveConfigCallback?.Invoke();
        _refreshOverlaysCallback?.Invoke();
    }

    private void TabletSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TabletImagesDataGrid.SelectedItem is not OverlayImage image) return;
        
        if (double.TryParse(TabletImageWidthTextBox.Text, out var width))
            image.Width = width;
        if (double.TryParse(TabletImageHeightTextBox.Text, out var height))
            image.Height = height;
        
        _saveConfigCallback?.Invoke();
        _refreshOverlaysCallback?.Invoke();
    }

    private void TabletMaintainAspectCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (TabletImagesDataGrid.SelectedItem is OverlayImage image)
        {
            image.MaintainAspectRatio = true;
            _saveConfigCallback?.Invoke();
            _refreshOverlaysCallback?.Invoke();
        }
    }

    private void TabletMaintainAspectCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (TabletImagesDataGrid.SelectedItem is OverlayImage image)
        {
            image.MaintainAspectRatio = false;
            _saveConfigCallback?.Invoke();
            _refreshOverlaysCallback?.Invoke();
        }
    }

    private void TabletLockedCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (TabletImagesDataGrid.SelectedItem is OverlayImage image)
        {
            image.IsLocked = true;
            _saveConfigCallback?.Invoke();
            _refreshOverlaysCallback?.Invoke();
        }
    }

    private void TabletLockedCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (TabletImagesDataGrid.SelectedItem is OverlayImage image)
        {
            image.IsLocked = false;
            _saveConfigCallback?.Invoke();
            _refreshOverlaysCallback?.Invoke();
        }
    }

    private void TabletRemoveImageBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TabletImagesDataGrid.SelectedItem is not OverlayImage selectedImage) return;

        _images.Remove(selectedImage);
        _saveConfigCallback?.Invoke();
        _refreshOverlaysCallback?.Invoke();
        ClearTabletPropertiesPanel();
    }

    private void TabletAddImageBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Image",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var filePath in dialog.FileNames)
            {
                var relativePath = MainWindow.GetRelativePath(filePath);
                var image = new OverlayImage
                {
                    FilePath = relativePath,
                    X = 100 + (_images.Count * 30),
                    Y = 100 + (_images.Count * 30)
                };
                
                _images.Add(image);
            }
            
            _saveConfigCallback?.Invoke();
            _refreshOverlaysCallback?.Invoke();
        }
    }

    private void TabletToggleOverlaysBtn_Click(object sender, RoutedEventArgs e)
    {
        _toggleOverlaysCallback?.Invoke();
    }

    private void UpdateTabletStopKeyDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((_settings.StopHotkeyModifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((_settings.StopHotkeyModifiers & 0x0001) != 0) parts.Add("Alt");
        if ((_settings.StopHotkeyModifiers & 0x0004) != 0) parts.Add("Shift");
        if ((_settings.StopHotkeyModifiers & 0x0008) != 0) parts.Add("Win");
        parts.Add(KeybindViewModel.VkToKeyName(_settings.StopHotkeyVirtualKey));
        TabletStopKeyText.Text = string.Join(" + ", parts);
    }

    private void UpdateTabletWtKeyDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((_settings.WindowToggle.HotkeyModifiers & 0x0010) != 0) parts.Add("RightCtrl");
        else if ((_settings.WindowToggle.HotkeyModifiers & 0x0020) != 0) parts.Add("LeftCtrl");
        else if ((_settings.WindowToggle.HotkeyModifiers & 0x0002) != 0) parts.Add("Ctrl");

        if ((_settings.WindowToggle.HotkeyModifiers & 0x0040) != 0) parts.Add("RightAlt");
        else if ((_settings.WindowToggle.HotkeyModifiers & 0x0080) != 0) parts.Add("LeftAlt");
        else if ((_settings.WindowToggle.HotkeyModifiers & 0x0001) != 0) parts.Add("Alt");

        if ((_settings.WindowToggle.HotkeyModifiers & 0x0100) != 0) parts.Add("RightShift");
        else if ((_settings.WindowToggle.HotkeyModifiers & 0x0200) != 0) parts.Add("LeftShift");
        else if ((_settings.WindowToggle.HotkeyModifiers & 0x0004) != 0) parts.Add("Shift");

        if ((_settings.WindowToggle.HotkeyModifiers & 0x0008) != 0) parts.Add("Win");

        parts.Add(KeybindViewModel.VkToKeyName(_settings.WindowToggle.HotkeyVirtualKey));
        TabletWtKeyText.Text = string.Join(" + ", parts);
    }

    private void UpdateTabletOtKeyDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((_settings.OverlayToggleModifiers & 0x0010) != 0) parts.Add("RightCtrl");
        else if ((_settings.OverlayToggleModifiers & 0x0020) != 0) parts.Add("LeftCtrl");
        else if ((_settings.OverlayToggleModifiers & 0x0002) != 0) parts.Add("Ctrl");

        if ((_settings.OverlayToggleModifiers & 0x0040) != 0) parts.Add("RightAlt");
        else if ((_settings.OverlayToggleModifiers & 0x0080) != 0) parts.Add("LeftAlt");
        else if ((_settings.OverlayToggleModifiers & 0x0001) != 0) parts.Add("Alt");

        if ((_settings.OverlayToggleModifiers & 0x0100) != 0) parts.Add("RightShift");
        else if ((_settings.OverlayToggleModifiers & 0x0200) != 0) parts.Add("LeftShift");
        else if ((_settings.OverlayToggleModifiers & 0x0004) != 0) parts.Add("Shift");

        if ((_settings.OverlayToggleModifiers & 0x0008) != 0) parts.Add("Win");

        parts.Add(KeybindViewModel.VkToKeyName(_settings.OverlayToggleVirtualKey));
        TabletOtKeyText.Text = string.Join(" + ", parts);
    }

    private void UpdateTabletCopilotKeyDisplay()
    {
        if (TabletCopilotKeyText != null)
        {
            TabletCopilotKeyText.Text = KeybindViewModel.VkToKeyName(_settings.CopilotVirtualKey);
        }
        
        if (TabletCopilotTriggerText != null)
        {
            var triggerParts = new System.Collections.Generic.List<string>();
            if ((_settings.CopilotTriggerModifiers & 0x0010) != 0) triggerParts.Add("RightCtrl");
            else if ((_settings.CopilotTriggerModifiers & 0x0020) != 0) triggerParts.Add("LeftCtrl");
            else if ((_settings.CopilotTriggerModifiers & 0x0002) != 0) triggerParts.Add("Ctrl");

            if ((_settings.CopilotTriggerModifiers & 0x0040) != 0) triggerParts.Add("RightAlt");
            else if ((_settings.CopilotTriggerModifiers & 0x0080) != 0) triggerParts.Add("LeftAlt");
            else if ((_settings.CopilotTriggerModifiers & 0x0001) != 0) triggerParts.Add("Alt");

            if ((_settings.CopilotTriggerModifiers & 0x0100) != 0) triggerParts.Add("RightShift");
            else if ((_settings.CopilotTriggerModifiers & 0x0200) != 0) triggerParts.Add("LeftShift");
            else if ((_settings.CopilotTriggerModifiers & 0x0004) != 0) triggerParts.Add("Shift");

            string keyName = KeybindViewModel.VkToKeyName(_settings.CopilotTriggerVirtualKey);
            if (!triggerParts.Contains(keyName))
            {
                triggerParts.Add(keyName);
            }

            TabletCopilotTriggerText.Text = string.Join(" + ", triggerParts);
        }
    }

    private void TabletGlobalKeybinds_Checked(object sender, RoutedEventArgs e)
    {
        _settings.GlobalEnabled = true;
        _saveConfigCallback?.Invoke();
        _refreshKeybindsCallback?.Invoke();
    }

    private void TabletGlobalKeybinds_Unchecked(object sender, RoutedEventArgs e)
    {
        _settings.GlobalEnabled = false;
        _saveConfigCallback?.Invoke();
        _refreshKeybindsCallback?.Invoke();
    }

    private void TabletGlobalSounds_Checked(object sender, RoutedEventArgs e)
    {
        _settings.SoundsEnabled = true;
        _saveConfigCallback?.Invoke();
        _refreshKeybindsCallback?.Invoke();
    }

    private void TabletGlobalSounds_Unchecked(object sender, RoutedEventArgs e)
    {
        _settings.SoundsEnabled = false;
        _saveConfigCallback?.Invoke();
        _refreshKeybindsCallback?.Invoke();
    }

    private void TabletAddKeybindBtn_Click(object sender, RoutedEventArgs e)
    {
        var entry = new KeybindEntry
        {
            Name = $"Keybind {_tabletKeybinds.Count + 1}",
            HotkeyVirtualKey = 0x30,
            Script = "/me text"
        };
        _settings.Entries.Add(entry);
        var vm = new KeybindViewModel(entry);
        _tabletKeybinds.Add(vm);
        TabletKeybindsDataGrid.SelectedItem = vm;
        _saveConfigCallback?.Invoke();
        _refreshKeybindsCallback?.Invoke();
    }

    private void TabletKeybindsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabletKeybindsDataGrid.SelectedItem is KeybindViewModel vm)
        {
            PopulateTabletKeybindPropertiesPanel(vm);
        }
        else
        {
            ClearTabletKeybindPropertiesPanel();
        }
    }

    private void PopulateTabletKeybindPropertiesPanel(KeybindViewModel vm)
    {
        _isPopulatingKeybindPanel = true;
        try
        {
            TabletKeybindPropertiesPanel.IsEnabled = true;
            TabletKeybindNoSelectionText.Visibility = Visibility.Collapsed;

            TabletKeybindNameTextBox.Text = vm.Name;
            TabletKeybindKeyText.Text = vm.KeyDisplay;
            TabletKeybindScriptTextBox.Text = vm.Entry.Script;
            TabletKeybindEnabledCheckBox.IsChecked = vm.Enabled;
        }
        finally
        {
            _isPopulatingKeybindPanel = false;
        }
    }

    private void ClearTabletKeybindPropertiesPanel()
    {
        _isPopulatingKeybindPanel = true;
        try
        {
            TabletKeybindPropertiesPanel.IsEnabled = false;
            TabletKeybindNoSelectionText.Visibility = Visibility.Visible;
            TabletKeybindNameTextBox.Text = string.Empty;
            TabletKeybindKeyText.Text = "None";
            TabletKeybindScriptTextBox.Text = string.Empty;
            TabletKeybindEnabledCheckBox.IsChecked = false;
        }
        finally
        {
            _isPopulatingKeybindPanel = false;
        }
    }

    private void TabletKeybindNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isPopulatingKeybindPanel) return;
        if (TabletKeybindsDataGrid.SelectedItem is KeybindViewModel vm)
        {
            vm.Name = TabletKeybindNameTextBox.Text;
            _saveConfigCallback?.Invoke();
            _refreshKeybindsCallback?.Invoke();
        }
    }

    private void TabletKeybindScriptTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isPopulatingKeybindPanel) return;
        if (TabletKeybindsDataGrid.SelectedItem is KeybindViewModel vm)
        {
            vm.Entry.Script = TabletKeybindScriptTextBox.Text;
            vm.Refresh();
            _saveConfigCallback?.Invoke();
            _refreshKeybindsCallback?.Invoke();
        }
    }

    private void TabletKeybindEnabledCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_isPopulatingKeybindPanel) return;
        if (TabletKeybindsDataGrid.SelectedItem is KeybindViewModel vm)
        {
            vm.Enabled = true;
            _saveConfigCallback?.Invoke();
            _refreshKeybindsCallback?.Invoke();
        }
    }

    private void TabletKeybindEnabledCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isPopulatingKeybindPanel) return;
        if (TabletKeybindsDataGrid.SelectedItem is KeybindViewModel vm)
        {
            vm.Enabled = false;
            _saveConfigCallback?.Invoke();
            _refreshKeybindsCallback?.Invoke();
        }
    }

    private void TabletRemoveKeybindBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TabletKeybindsDataGrid.SelectedItem is KeybindViewModel vm)
        {
            _settings.Entries.Remove(vm.Entry);
            _tabletKeybinds.Remove(vm);
            ClearTabletKeybindPropertiesPanel();
            _saveConfigCallback?.Invoke();
            _refreshKeybindsCallback?.Invoke();
        }
    }

    private void TabletRecordKeybindBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TabletKeybindsDataGrid.SelectedItem is KeybindViewModel vm)
        {
            _recordingKeybindEntry = vm;
            _isRecordingTabletKeybind = true;
            TabletRecordKeybindBtn.Content = "Listening...";
        }
    }

    private void TabletDelays_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (TabletDelayMsTextBox == null || TabletDelayChatOpenMsTextBox == null || 
            TabletDelayTextMsTextBox == null || TabletDelayEnterMsTextBox == null) return;

        if (int.TryParse(TabletDelayMsTextBox.Text, out var delay))
            _settings.DelayMs = delay;
        if (int.TryParse(TabletDelayChatOpenMsTextBox.Text, out var chatOpen))
            _settings.DelayAfterChatOpenMs = chatOpen;
        if (int.TryParse(TabletDelayTextMsTextBox.Text, out var textDelay))
            _settings.DelayAfterTextMs = textDelay;
        if (int.TryParse(TabletDelayEnterMsTextBox.Text, out var enterDelay))
            _settings.DelayAfterEnterMs = enterDelay;

        _saveConfigCallback?.Invoke();
        _refreshKeybindsCallback?.Invoke();
    }

    private void TabletStopHotkey_Checked(object sender, RoutedEventArgs e)
    {
        _settings.StopHotkeyEnabled = true;
        _saveConfigCallback?.Invoke();
        _refreshKeybindsCallback?.Invoke();
    }

    private void TabletStopHotkey_Unchecked(object sender, RoutedEventArgs e)
    {
        _settings.StopHotkeyEnabled = false;
        _saveConfigCallback?.Invoke();
        _refreshKeybindsCallback?.Invoke();
    }

    private void TabletRecordStopKeyBtn_Click(object sender, RoutedEventArgs e)
    {
        _isRecordingTabletStopKey = true;
        TabletRecordStopKeyBtn.Content = "Listening...";
    }

    private void TabletWt_Checked(object sender, RoutedEventArgs e)
    {
        _settings.WindowToggle.Enabled = true;
        _saveConfigCallback?.Invoke();
        _refreshKeybindsCallback?.Invoke();
    }

    private void TabletWt_Unchecked(object sender, RoutedEventArgs e)
    {
        _settings.WindowToggle.Enabled = false;
        _saveConfigCallback?.Invoke();
        _refreshKeybindsCallback?.Invoke();
    }

    private void TabletWtTitleComboBox_DropDownOpened(object sender, EventArgs e)
    {
        PopulateWindowTitles();
    }

    private void PopulateWindowTitles()
    {
        TabletWtTitleComboBox.Items.Clear();
        var processes = System.Diagnostics.Process.GetProcesses();
        var titles = processes
            .Select(p => p.MainWindowTitle)
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .OrderBy(t => t);

        foreach (var title in titles)
        {
            TabletWtTitleComboBox.Items.Add(title);
        }
    }

    private void TabletWtTitleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabletWtTitleComboBox.SelectedItem is string selectedTitle)
        {
            _settings.WindowToggle.WindowTitle = selectedTitle;
            _saveConfigCallback?.Invoke();
            _refreshKeybindsCallback?.Invoke();
        }
    }

    private void TabletWtTitleComboBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _settings.WindowToggle.WindowTitle = TabletWtTitleComboBox.Text;
        _saveConfigCallback?.Invoke();
        _refreshKeybindsCallback?.Invoke();
    }

    private void TabletRecordWtKeyBtn_Click(object sender, RoutedEventArgs e)
    {
        _isRecordingTabletWtKey = true;
        TabletRecordWtKeyBtn.Content = "Listening...";
    }

    private void TabletWtCoords_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (TabletWtXTextBox == null || TabletWtYTextBox == null || TabletWtWidthTextBox == null || TabletWtHeightTextBox == null) return;
        
        _settings.WindowToggle.WindowTitle = TabletWtTitleComboBox.Text;

        if (int.TryParse(TabletWtXTextBox.Text, out var x))
            _settings.WindowToggle.X = x;
        if (int.TryParse(TabletWtYTextBox.Text, out var y))
            _settings.WindowToggle.Y = y;
        if (int.TryParse(TabletWtWidthTextBox.Text, out var w))
            _settings.WindowToggle.Width = w;
        if (int.TryParse(TabletWtHeightTextBox.Text, out var h))
            _settings.WindowToggle.Height = h;

        _saveConfigCallback?.Invoke();
        _refreshKeybindsCallback?.Invoke();
    }

    private void TabletOt_Checked(object sender, RoutedEventArgs e)
    {
        _settings.OverlayToggleEnabled = true;
        _saveConfigCallback?.Invoke();
        _refreshKeybindsCallback?.Invoke();
    }

    private void TabletOt_Unchecked(object sender, RoutedEventArgs e)
    {
        _settings.OverlayToggleEnabled = false;
        _saveConfigCallback?.Invoke();
        _refreshKeybindsCallback?.Invoke();
    }

    private void TabletRecordOtKeyBtn_Click(object sender, RoutedEventArgs e)
    {
        _isRecordingTabletOtKey = true;
        TabletRecordOtKeyBtn.Content = "Listening...";
    }

    private void TabletStartWithWindows_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        _settings.StartWithWindows = true;
        StartupService.EnableStartup();
        _saveConfigCallback?.Invoke();
    }

    private void TabletStartWithWindows_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        _settings.StartWithWindows = false;
        StartupService.DisableStartup();
        _saveConfigCallback?.Invoke();
    }

    private void TabletCopilot_Checked(object sender, RoutedEventArgs e)
    {
        _settings.CopilotEnabled = true;
        _saveConfigCallback?.Invoke();
        _refreshKeybindsCallback?.Invoke();
    }

    private void TabletCopilot_Unchecked(object sender, RoutedEventArgs e)
    {
        _settings.CopilotEnabled = false;
        _saveConfigCallback?.Invoke();
        _refreshKeybindsCallback?.Invoke();
    }

    private void TabletRecordCopilotKeyBtn_Click(object sender, RoutedEventArgs e)
    {
        _isRecordingTabletCopilotKey = true;
        TabletRecordCopilotKeyBtn.Content = "Listening...";
    }

    private void TabletRecordCopilotTriggerBtn_Click(object sender, RoutedEventArgs e)
    {
        _isRecordingTabletCopilotTrigger = true;
        TabletRecordCopilotTriggerBtn.Content = "Listening...";
    }

    private void TabletLockBtn_Click(object sender, RoutedEventArgs e)
    {
        LockTablet();
    }

    private void LockscreenGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        UnlockTablet();
    }

    private void LockTablet()
    {
        LockscreenGrid.Visibility = Visibility.Visible;
        StopDutyAutoRefresh();
    }

    private void UnlockTablet()
    {
        LockscreenGrid.Visibility = Visibility.Collapsed;
        if (DutySidebarBorder != null && DutySidebarBorder.Visibility == Visibility.Visible)
        {
            StartDutyAutoRefresh();
            _ = RefreshOnDutyDataAsync(showLoading: false);
        }
    }

    private void LoadWallpaper()
    {
        // 1. Desktop Wallpaper
        if (string.IsNullOrEmpty(_settings.TabletWallpaperPath) || !File.Exists(_settings.TabletWallpaperPath))
        {
            DesktopWallpaperImage.Source = null;
        }
        else
        {
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(_settings.TabletWallpaperPath);
                bitmap.EndInit();
                DesktopWallpaperImage.Source = bitmap;
            }
            catch
            {
                DesktopWallpaperImage.Source = null;
            }
        }

        // 2. Lockscreen Wallpaper
        if (string.IsNullOrEmpty(_settings.TabletLockscreenWallpaperPath) || !File.Exists(_settings.TabletLockscreenWallpaperPath))
        {
            LockscreenWallpaperImage.Source = null;
        }
        else
        {
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(_settings.TabletLockscreenWallpaperPath);
                bitmap.EndInit();
                LockscreenWallpaperImage.Source = bitmap;
            }
            catch
            {
                LockscreenWallpaperImage.Source = null;
            }
        }
    }

    private void OpenApp(Grid appGrid, bool showBrowserToolbar = false)
    {
        DesktopGrid.Visibility = Visibility.Collapsed;
        WebTabContent.Visibility = Visibility.Collapsed;
        OverlaysTabContent.Visibility = Visibility.Collapsed;
        KeybindsTabContent.Visibility = Visibility.Collapsed;
        SettingsTabContent.Visibility = Visibility.Collapsed;

        appGrid.Visibility = Visibility.Visible;
        NavToolbarBorder.Visibility = showBrowserToolbar ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseAllAppsAndGoHome()
    {
        WebTabContent.Visibility = Visibility.Collapsed;
        OverlaysTabContent.Visibility = Visibility.Collapsed;
        KeybindsTabContent.Visibility = Visibility.Collapsed;
        SettingsTabContent.Visibility = Visibility.Collapsed;
        NavToolbarBorder.Visibility = Visibility.Collapsed;

        DesktopGrid.Visibility = Visibility.Visible;
    }

    private void EnsureWebViewInitialized()
    {
        if (!_webViewInitialized)
        {
            _webViewInitialized = true;
            InitializeWebView();
        }
        else
        {
            NavigateTo(UrlTextBox.Text);
        }
    }

    private void BrowserMdApp_Click(object sender, RoutedEventArgs e)
    {
        UrlTextBox.Text = "https://md.b-zone.ro";
        OpenApp(WebTabContent, true);
        EnsureWebViewInitialized();
    }

    private void RpgPanelApp_Click(object sender, RoutedEventArgs e)
    {
        UrlTextBox.Text = "https://gta5.ro";
        OpenApp(WebTabContent, true);
        EnsureWebViewInitialized();
    }

    private void MaiApp_Click(object sender, RoutedEventArgs e)
    {
        UrlTextBox.Text = "https://mai.b-zone.ro";
        OpenApp(WebTabContent, true);
        EnsureWebViewInitialized();
    }

    private void PrimarieApp_Click(object sender, RoutedEventArgs e)
    {
        UrlTextBox.Text = "https://primarie.b-zone-business.ro/";
        OpenApp(WebTabContent, true);
        EnsureWebViewInitialized();
    }

    private void JudecatorieApp_Click(object sender, RoutedEventArgs e)
    {
        UrlTextBox.Text = "https://judecatorie-b-zone.ro/";
        OpenApp(WebTabContent, true);
        EnsureWebViewInitialized();
    }

    private void OverlaysApp_Click(object sender, RoutedEventArgs e)
    {
        OpenApp(OverlaysTabContent, false);
    }

    private void KeybindsApp_Click(object sender, RoutedEventArgs e)
    {
        OpenApp(KeybindsTabContent, false);
    }

    private void SettingsApp_Click(object sender, RoutedEventArgs e)
    {
        OpenApp(SettingsTabContent, false);
    }

    // ==========================================
    // ON-DUTY MEDICS DISPATCH SYSTEM (SIDEBAR)
    // ==========================================

    private void OnDutyApp_Click(object sender, RoutedEventArgs e)
    {
        DutySidebarToggle_Click(sender, e);
    }

    private void DutySidebarCollapseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_isSidebarExpanded) return;
        _isSidebarExpanded = false;

        if (DutySidebarBorder != null && DutySidebarCol != null)
        {
            DutySidebarBorder.Visibility = Visibility.Collapsed;
            DutySidebarCol.Width = new GridLength(0);
            Width = Math.Max(1200, Width - DutySidebarTotalWidth);
            Left += DutySidebarTotalWidth;
            if (DutyExpandTabBtn != null) DutyExpandTabBtn.Visibility = Visibility.Visible;
            StopDutyAutoRefresh();
        }
    }

    private void DutySidebarExpandBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isSidebarExpanded) return;
        _isSidebarExpanded = true;

        if (DutySidebarBorder != null && DutySidebarCol != null)
        {
            double minLeft = SystemParameters.VirtualScreenLeft;
            double currentLeft = Left;
            double targetLeft = currentLeft - DutySidebarTotalWidth;
            if (targetLeft < minLeft)
            {
                targetLeft = minLeft;
            }

            Left = targetLeft;
            Width += DutySidebarTotalWidth;
            DutySidebarCol.Width = new GridLength(DutySidebarTotalWidth);
            DutySidebarBorder.Visibility = Visibility.Visible;
            if (DutyExpandTabBtn != null) DutyExpandTabBtn.Visibility = Visibility.Collapsed;

            StartDutyAutoRefresh();
            if (_currentDutyResult == null || (DateTime.Now - _currentDutyResult.LastUpdated).TotalSeconds > 30)
            {
                _ = RefreshOnDutyDataAsync(showLoading: _currentDutyResult == null);
            }
        }
    }

    private void DutySidebarToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isSidebarExpanded)
        {
            DutySidebarCollapseBtn_Click(sender, e);
        }
        else
        {
            DutySidebarExpandBtn_Click(sender, e);
        }
    }

    private async void DutyRefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DutyRefreshBtn != null) DutyRefreshBtn.IsEnabled = false;
        try
        {
            await RefreshOnDutyDataAsync(showLoading: true);
        }
        finally
        {
            if (DutyRefreshBtn != null) DutyRefreshBtn.IsEnabled = true;
        }
    }

    private void StartDutyAutoRefresh()
    {
        if (_onDutyRefreshTimer == null)
        {
            _onDutyRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(25)
            };
            _onDutyRefreshTimer.Tick += async (s, e) =>
            {
                if (Visibility == Visibility.Visible && DutySidebarBorder != null && DutySidebarBorder.Visibility == Visibility.Visible && LockscreenGrid != null && LockscreenGrid.Visibility != Visibility.Visible)
                {
                    await RefreshOnDutyDataAsync(showLoading: false);
                }
            };
        }
        _onDutyRefreshTimer.Start();
    }

    private void StopDutyAutoRefresh()
    {
        _onDutyRefreshTimer?.Stop();
    }

    private async Task RefreshOnDutyDataAsync(bool showLoading = true)
    {
        if (showLoading && _currentDutyResult == null && DutyLoadingIndicator != null)
        {
            DutyLoadingIndicator.Visibility = Visibility.Visible;
        }

        if (DutyErrorState != null) DutyErrorState.Visibility = Visibility.Collapsed;

        var result = await _onDutyService.FetchOnDutyDataAsync();
        if (DutyLoadingIndicator != null) DutyLoadingIndicator.Visibility = Visibility.Collapsed;

        if (result.IsSuccess)
        {
            _currentDutyResult = result;
            if (DutyLastUpdatedText != null) DutyLastUpdatedText.Text = $"Sincronizat: {result.LastUpdated:HH:mm:ss}";
            if (DutySidebarTotalBadge != null) DutySidebarTotalBadge.Text = $"({result.TotalCount})";
            if (DutyTitleBarToggleBtn != null) DutyTitleBarToggleBtn.Content = $"🟢 {result.TotalCount} Medici Duty";

            ApplyDutyFilterAndSearch();
        }
        else
        {
            if (_currentDutyResult == null)
            {
                if (DutyErrorMessageText != null) DutyErrorMessageText.Text = result.ErrorMessage ?? "Eroare necunoscută la preluarea datelor.";
                if (DutyErrorState != null) DutyErrorState.Visibility = Visibility.Visible;
                if (DutyEmptyState != null) DutyEmptyState.Visibility = Visibility.Collapsed;
            }
            else
            {
                if (DutyLastUpdatedText != null) DutyLastUpdatedText.Text = $"Eroare actualizare: {DateTime.Now:HH:mm:ss}";
            }
        }
    }

    private void DutySearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DutySearchTextBox == null || DutySearchPlaceholder == null || DutySearchClearBtn == null) return;

        _dutySearchText = DutySearchTextBox.Text.Trim();
        DutySearchPlaceholder.Visibility = string.IsNullOrEmpty(_dutySearchText) ? Visibility.Visible : Visibility.Collapsed;
        DutySearchClearBtn.Visibility = string.IsNullOrEmpty(_dutySearchText) ? Visibility.Collapsed : Visibility.Visible;

        ApplyDutyFilterAndSearch();
    }

    private void DutySearchClearBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DutySearchTextBox != null)
        {
            DutySearchTextBox.Text = string.Empty;
        }
    }

    private void DutyFilterRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (DutyGroupsItemsControl == null) return;

        if (sender is RadioButton rb && rb.Content != null)
        {
            _dutyRankFilter = rb.Content.ToString() ?? "Toți";
            ApplyDutyFilterAndSearch();
        }
    }

    private void ApplyDutyFilterAndSearch()
    {
        if (DutyGroupsItemsControl == null) return;

        if (_currentDutyResult == null || !_currentDutyResult.IsSuccess)
        {
            DutyGroupsItemsControl.ItemsSource = null;
            return;
        }

        var query = _dutySearchText.ToLowerInvariant();
        var filter = _dutyRankFilter;

        var filteredGroups = new List<OnDutyGroup>();

        foreach (var group in _currentDutyResult.Groups)
        {
            // Rank filter check (strictly Asistent - Primar)
            if (filter != "Toți")
            {
                if ((filter == "Primari" || filter == "Medici Primari") && !group.Grad.Contains("Primar", StringComparison.OrdinalIgnoreCase)) continue;
                if ((filter == "Specialiști" || filter == "Medici Specialiști") && !group.Grad.Contains("Speciali", StringComparison.OrdinalIgnoreCase)) continue;
                if ((filter == "Rezidenți" || filter == "Medici Rezidenți") && !group.Grad.Contains("Reziden", StringComparison.OrdinalIgnoreCase)) continue;
                if ((filter == "Stagiari" || filter == "Medici Stagiari") && !group.Grad.Contains("Stagiar", StringComparison.OrdinalIgnoreCase)) continue;
                if ((filter == "Asistenți" || filter == "Asistenți Medicali") && !group.Grad.Contains("Asistent", StringComparison.OrdinalIgnoreCase)) continue;
            }

            // Search query filter (Matches Name, Indicativ or CNP)
            var matchingDoctors = group.Doctors.Where(d =>
                string.IsNullOrWhiteSpace(query) ||
                d.Nume.ToLowerInvariant().Contains(query) ||
                d.Indicativ.ToLowerInvariant().Contains(query) ||
                d.Cnp.Contains(query) ||
                d.Grad.ToLowerInvariant().Contains(query)
            ).ToList();

            if (matchingDoctors.Any())
            {
                filteredGroups.Add(new OnDutyGroup
                {
                    Grad = group.Grad,
                    GradOrder = group.GradOrder,
                    GradColor = group.GradColor,
                    Doctors = new ObservableCollection<OnDutyDoctor>(matchingDoctors)
                });
            }
        }

        DutyGroupsItemsControl.ItemsSource = filteredGroups;

        int totalFound = filteredGroups.Sum(g => g.Count);
        if (DutyEmptyState != null)
        {
            if (totalFound == 0 && _currentDutyResult.TotalCount > 0)
            {
                DutyEmptyState.Visibility = Visibility.Visible;
            }
            else
            {
                DutyEmptyState.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void ShowDutyCopyToast(string message)
    {
        DutyCopyToastText.Text = message;
        DutyCopyToastBorder.Visibility = Visibility.Visible;

        _dutyToastTimer?.Stop();
        _dutyToastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.5)
        };
        _dutyToastTimer.Tick += (s, e) =>
        {
            DutyCopyToastBorder.Visibility = Visibility.Collapsed;
            _dutyToastTimer.Stop();
        };
        _dutyToastTimer.Start();
    }

    private void TabletSelectWallpaperBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Alege Wallpaper Desktop",
            Filter = "Imagini (*.png;*.gif;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.gif;*.jpg;*.jpeg;*.bmp;*.webp"
        };

        if (dialog.ShowDialog() == true)
        {
            _settings.TabletWallpaperPath = dialog.FileName;
            _saveConfigCallback?.Invoke();
            LoadWallpaper();
        }
    }

    private void TabletResetWallpaperBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.TabletWallpaperPath = string.Empty;
        _saveConfigCallback?.Invoke();
        LoadWallpaper();
    }

    private void TabletSelectLockWallpaperBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Alege Wallpaper Lockscreen",
            Filter = "Imagini (*.png;*.gif;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.gif;*.jpg;*.jpeg;*.bmp;*.webp"
        };

        if (dialog.ShowDialog() == true)
        {
            _settings.TabletLockscreenWallpaperPath = dialog.FileName;
            _saveConfigCallback?.Invoke();
            LoadWallpaper();
        }
    }

    private void TabletResetLockWallpaperBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.TabletLockscreenWallpaperPath = string.Empty;
        _saveConfigCallback?.Invoke();
        LoadWallpaper();
    }

    private void LoadSettingsIntoUi()
    {
        // Bind Keybinds
        _tabletKeybinds.Clear();
        foreach (var entry in _settings.Entries)
        {
            _tabletKeybinds.Add(new KeybindViewModel(entry));
        }

        TabletGlobalKeybindsEnabledCheckBox.IsChecked = _settings.GlobalEnabled;
        TabletGlobalSoundsEnabledCheckBox.IsChecked = _settings.SoundsEnabled;
        TabletDelayMsTextBox.Text = _settings.DelayMs.ToString();
        TabletDelayChatOpenMsTextBox.Text = _settings.DelayAfterChatOpenMs.ToString();
        TabletDelayTextMsTextBox.Text = _settings.DelayAfterTextMs.ToString();
        TabletDelayEnterMsTextBox.Text = _settings.DelayAfterEnterMs.ToString();

        TabletStopHotkeyEnabledCheckBox.IsChecked = _settings.StopHotkeyEnabled;
        UpdateTabletStopKeyDisplay();

        TabletWtEnabledCheckBox.IsChecked = _settings.WindowToggle.Enabled;
        TabletWtTitleComboBox.Text = _settings.WindowToggle.WindowTitle;
        TabletWtXTextBox.Text = _settings.WindowToggle.X.ToString();
        TabletWtYTextBox.Text = _settings.WindowToggle.Y.ToString();
        TabletWtWidthTextBox.Text = _settings.WindowToggle.Width.ToString();
        TabletWtHeightTextBox.Text = _settings.WindowToggle.Height.ToString();
        UpdateTabletWtKeyDisplay();

        TabletOtEnabledCheckBox.IsChecked = _settings.OverlayToggleEnabled;
        UpdateTabletOtKeyDisplay();

        bool isStartup = StartupService.IsStartupEnabled();
        _settings.StartWithWindows = isStartup;
        TabletStartWithWindowsCheckBox.IsChecked = isStartup;

        TabletCopilotEnabledCheckBox.IsChecked = _settings.CopilotEnabled;
        UpdateTabletCopilotKeyDisplay();

        if (SettingsCurrentVersionText != null)
        {
            SettingsCurrentVersionText.Text = $"v{UpdateService.GetCurrentVersion()}";
        }

        LoadWallpaper();
    }

    private void CopySettings(KeybindSettings source, KeybindSettings dest)
    {
        dest.GlobalEnabled = source.GlobalEnabled;
        dest.SoundsEnabled = source.SoundsEnabled;
        dest.DelayMs = source.DelayMs;
        dest.DelayAfterChatOpenMs = source.DelayAfterChatOpenMs;
        dest.DelayAfterTextMs = source.DelayAfterTextMs;
        dest.DelayAfterEnterMs = source.DelayAfterEnterMs;
        dest.StopHotkeyEnabled = source.StopHotkeyEnabled;
        dest.StopHotkeyVirtualKey = source.StopHotkeyVirtualKey;
        dest.StopHotkeyModifiers = source.StopHotkeyModifiers;

        dest.WindowToggle.Enabled = source.WindowToggle.Enabled;
        dest.WindowToggle.WindowTitle = source.WindowToggle.WindowTitle;
        dest.WindowToggle.X = source.WindowToggle.X;
        dest.WindowToggle.Y = source.WindowToggle.Y;
        dest.WindowToggle.Width = source.WindowToggle.Width;
        dest.WindowToggle.Height = source.WindowToggle.Height;
        dest.WindowToggle.HotkeyVirtualKey = source.WindowToggle.HotkeyVirtualKey;
        dest.WindowToggle.HotkeyModifiers = source.WindowToggle.HotkeyModifiers;

        dest.OverlayToggleEnabled = source.OverlayToggleEnabled;
        dest.OverlayToggleVirtualKey = source.OverlayToggleVirtualKey;
        dest.OverlayToggleModifiers = source.OverlayToggleModifiers;

        dest.StartWithWindows = source.StartWithWindows;
        dest.TabletWallpaperPath = source.TabletWallpaperPath;
        dest.TabletLockscreenWallpaperPath = source.TabletLockscreenWallpaperPath;

        dest.CopilotEnabled = source.CopilotEnabled;
        dest.CopilotVirtualKey = source.CopilotVirtualKey;
        dest.CopilotTriggerVirtualKey = source.CopilotTriggerVirtualKey;
        dest.CopilotTriggerModifiers = source.CopilotTriggerModifiers;

        dest.Entries.Clear();
        foreach (var entry in source.Entries)
        {
            dest.Entries.Add(entry);
        }
    }

    private void TabletImportConfigBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            Title = "Import Configuration"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var configService = new ConfigurationService();
                var config = configService.ImportConfiguration(dialog.FileName);
                if (config != null)
                {
                    CopySettings(config.Keybind, _settings);
                    
                    _images.Clear();
                    if (config.Images != null)
                    {
                        foreach (var img in config.Images)
                        {
                            _images.Add(img);
                        }
                    }

                    _saveConfigCallback?.Invoke();
                    LoadSettingsIntoUi();

                    _refreshKeybindsCallback?.Invoke();
                    _refreshOverlaysCallback?.Invoke();

                    MessageBox.Show("Configurare importată cu succes în tabletă!", "Succes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Fișierul selectat nu este o configurare validă.", "Eroare Import", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Eroare la importul configurării: {ex.Message}", "Eroare Import", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void TabletExportConfigBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = "json",
            Title = "Export Configuration"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var config = new AppConfiguration
                {
                    Keybind = _settings,
                    Images = _images.ToList()
                };
                var configService = new ConfigurationService();
                configService.ExportConfiguration(config, dialog.FileName);
                MessageBox.Show("Configurare exportată cu succes!", "Succes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Eroare la exportul configurării: {ex.Message}", "Eroare Export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void TabletExitAppBtn_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Sigur doriți să închideți complet MDTab-Helper?", "Închidere MDTab-Helper", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            _saveConfigCallback?.Invoke();
            if (System.Windows.Application.Current.MainWindow is MainWindow mw)
            {
                mw.ExitApplicationDirectly();
            }
            else
            {
                System.Windows.Application.Current.Shutdown();
            }
        }
    }

    #region Auto-Update Logic

    private async Task CheckForUpdatesSilentlyAsync()
    {
        try
        {
            var update = await _updateService.CheckForUpdatesAsync();
            if (update.IsUpdateAvailable)
            {
                _availableUpdate = update;
                TabletUpdateBadgeBtn.Content = $"⚡ Update v{update.NewVersion}";
                TabletUpdateBadgeBtn.Visibility = Visibility.Visible;
                if (SettingsUpdateStatusText != null)
                {
                    SettingsUpdateStatusText.Text = $"Versiune nouă disponibilă: v{update.NewVersion}";
                    SettingsUpdateStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                }
            }
            else
            {
                if (SettingsUpdateStatusText != null)
                {
                    SettingsUpdateStatusText.Text = "Ești pe cea mai recentă versiune.";
                    SettingsUpdateStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
                }
            }
        }
        catch
        {
            // Silently ignore background check errors
        }
    }

    private async void SettingsCheckUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsCheckUpdateBtn != null) SettingsCheckUpdateBtn.IsEnabled = false;
        if (SettingsUpdateStatusText != null) SettingsUpdateStatusText.Text = "Se verifică GitHub...";

        try
        {
            var update = await _updateService.CheckForUpdatesAsync();
            if (update.IsUpdateAvailable)
            {
                _availableUpdate = update;
                TabletUpdateBadgeBtn.Content = $"⚡ Update v{update.NewVersion}";
                TabletUpdateBadgeBtn.Visibility = Visibility.Visible;
                if (SettingsUpdateStatusText != null)
                {
                    SettingsUpdateStatusText.Text = $"Versiune nouă disponibilă: v{update.NewVersion}!";
                    SettingsUpdateStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                }
                ShowUpdateModal(update);
            }
            else
            {
                if (SettingsUpdateStatusText != null)
                {
                    SettingsUpdateStatusText.Text = $"Ești la zi (v{UpdateService.GetCurrentVersion()})!";
                    SettingsUpdateStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                }
                MessageBox.Show($"MDTab-Helper este la zi!\nVersiunea curentă: v{UpdateService.GetCurrentVersion()}", "Actualizări", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            if (SettingsUpdateStatusText != null) SettingsUpdateStatusText.Text = "Eroare la verificare.";
            MessageBox.Show($"Eroare la verificarea actualizărilor: {ex.Message}", "Eroare Actualizare", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (SettingsCheckUpdateBtn != null) SettingsCheckUpdateBtn.IsEnabled = true;
        }
    }

    private void TabletUpdateBadgeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate != null)
        {
            ShowUpdateModal(_availableUpdate);
        }
        else
        {
            SettingsCheckUpdateBtn_Click(sender, e);
        }
    }

    private void ShowUpdateModal(UpdateInfo update)
    {
        UpdateModalVersionText.Text = $"v{update.NewVersion}";
        if (update.FileSizeBytes > 0)
        {
            UpdateModalSizeText.Text = $"({update.FileSizeBytes / (1024.0 * 1024.0):F1} MB)";
        }
        else
        {
            UpdateModalSizeText.Text = "";
        }

        UpdateChangelogText.Text = string.IsNullOrWhiteSpace(update.Changelog) 
            ? "Îmbunătățiri de performanță, stabilitate și remedieri." 
            : update.Changelog;

        UpdateProgressPanel.Visibility = Visibility.Collapsed;
        UpdateActionButtonsGrid.Visibility = Visibility.Visible;
        ConfirmUpdateBtn.IsEnabled = true;
        CancelUpdateBtn.IsEnabled = true;
        CancelUpdateBtn.Content = "Mai târziu";
        UpdateHintText.Text = "Aplicația se va reporni automat după descărcare.";

        UpdateModalGrid.Visibility = Visibility.Visible;
    }

    private void CancelUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        _updateCts?.Cancel();
        UpdateModalGrid.Visibility = Visibility.Collapsed;
    }

    private async void ConfirmUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate == null || string.IsNullOrEmpty(_availableUpdate.DownloadUrl)) return;

        ConfirmUpdateBtn.IsEnabled = false;
        CancelUpdateBtn.Content = "Anulează";
        UpdateProgressPanel.Visibility = Visibility.Visible;
        UpdateProgressBar.Value = 0;
        UpdateProgressStatusText.Text = "Pornire descărcare...";

        _updateCts?.Dispose();
        _updateCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<(long downloaded, long total)>(p =>
            {
                if (p.total > 0)
                {
                    double pct = (double)p.downloaded / p.total * 100.0;
                    UpdateProgressBar.Value = Math.Min(100.0, Math.Max(0.0, pct));
                    double mbDown = p.downloaded / (1024.0 * 1024.0);
                    double mbTotal = p.total / (1024.0 * 1024.0);
                    UpdateProgressStatusText.Text = $"Se descarcă... {pct:F0}% ({mbDown:F1} MB / {mbTotal:F1} MB)";
                }
                else
                {
                    double mbDown = p.downloaded / (1024.0 * 1024.0);
                    UpdateProgressStatusText.Text = $"Se descarcă... ({mbDown:F1} MB)";
                }
            });

            string tempFile = await _updateService.DownloadUpdateAsync(_availableUpdate.DownloadUrl, progress, _updateCts.Token);

            UpdateProgressStatusText.Text = "Descărcare finalizată! Se repornește aplicația...";
            UpdateProgressBar.Value = 100;

            // Save configuration before exiting
            _saveConfigCallback?.Invoke();

            await Task.Delay(800);

            UpdateService.ApplyUpdateAndRestart(tempFile);
        }
        catch (OperationCanceledException)
        {
            UpdateProgressStatusText.Text = "Descărcare anulată.";
            UpdateProgressPanel.Visibility = Visibility.Collapsed;
            ConfirmUpdateBtn.IsEnabled = true;
            CancelUpdateBtn.Content = "Mai târziu";
        }
        catch (Exception ex)
        {
            UpdateProgressStatusText.Text = "Eroare la descărcare.";
            MessageBox.Show($"Eroare la descărcarea noii versiuni: {ex.Message}", "Eroare Actualizare", MessageBoxButton.OK, MessageBoxImage.Error);
            ConfirmUpdateBtn.IsEnabled = true;
            CancelUpdateBtn.Content = "Mai târziu";
        }
    }

    #endregion
}
