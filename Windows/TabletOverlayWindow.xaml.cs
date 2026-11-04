using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
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
    private bool _isUpdatingImagePropertiesUi = false;

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
    private DispatcherTimer? _keepAliveTimer;
    private bool _isRefreshingDuty = false;
    private DispatcherTimer? _dutyToastTimer;
    private OnDutyResult? _currentDutyResult;
    private string _dutySearchText = string.Empty;
    private string _dutyRankFilter = "Toți";
    private bool _isSidebarExpanded = false;
    private const double DutySidebarTotalWidth = 360;
    private readonly UpdateService _updateService = new();
    private UpdateInfo? _availableUpdate;
    private CancellationTokenSource? _updateCts;
    private bool _wasWebViewVisibleBeforeModal = false;
    private bool _wasWebViewVisibleBeforeLock = false;

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
        if (_images.Count > 0)
        {
            TabletImagesDataGrid.SelectedIndex = 0;
        }
        else
        {
            ClearTabletPropertiesPanel();
        }

        // Bind Keybinds
        _tabletKeybinds = new ObservableCollection<KeybindViewModel>();
        foreach (var entry in _settings.Entries)
        {
            _tabletKeybinds.Add(CreateKeybindViewModel(entry));
        }
        TabletKeybindsDataGrid.ItemsSource = _tabletKeybinds;
        TabletKeybindsDataGrid.CellEditEnding += TabletKeybindsDataGrid_CellEditEnding;

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

        _onDutyService.OnCookiesReceivedAsync = HandleSetCookiesFromApiAsync;

        _keepAliveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(50)
        };
        _keepAliveTimer.Tick += KeepAliveTimer_Tick;
        _keepAliveTimer.Start();

        // Check if user is connected to MD Panel right from tablet start!
        EnsureWebViewInitialized("https://md.b-zone.ro");
    }

    private void ClockTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm:ss");

        if (LockscreenGreetingText != null)
        {
            LockscreenGreetingText.Text = GetTimeBasedGreeting(now);
        }
        if (LockscreenClockText != null)
        {
            LockscreenClockText.Text = now.ToString("HH:mm");
        }
        if (LockscreenDateText != null)
        {
            var culture = new System.Globalization.CultureInfo("ro-RO");
            LockscreenDateText.Text = now.ToString("dddd, d MMMM", culture);
        }

        if (DutyTitleBarToggleBtn != null)
        {
            bool hasData = _currentDutyResult != null && _currentDutyResult.IsSuccess;
            DutyTitleBarToggleBtn.Opacity = hasData ? 1.0 : 0.85;
            DutyTitleBarToggleBtn.ToolTip = hasData
                ? "Comută afișarea listei laterale de Medici la Datorie"
                : "Medici Duty (Necesită conectare pe portal)";
        }



        if (now.Minute % 10 == 0 && now.Second == 0)
        {
            _ = CheckForUpdatesSilentlyAsync();
        }
    }

    private string _pendingUrl = string.Empty;

    private async void InitializeWebView(string? targetUrl = null)
    {
        try
        {
            string dest = !string.IsNullOrWhiteSpace(targetUrl) 
                ? targetUrl 
                : (!string.IsNullOrWhiteSpace(_pendingUrl) ? _pendingUrl : _settings.TabletUrl);

            // Set up a persistent User Data folder so sessions and logins are memorized
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string userFolder = Path.Combine(appData, "MDTab-Helper", "TabletCache");
            Directory.CreateDirectory(userFolder);

            var envOptions = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--disable-background-timer-throttling --disable-backgrounding-occluded-windows --disable-renderer-backgrounding --autoplay-policy=no-user-gesture-required --enable-gpu-rasterization --enable-zero-copy --ignore-gpu-blocklist"
            };
            var env = await CoreWebView2Environment.CreateAsync(null, userFolder, envOptions);
            await TabletWebView.EnsureCoreWebView2Async(env);

            try
            {
                await TabletWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                    try {
                        Object.defineProperty(document, 'hidden', { get: () => false, configurable: true });
                        Object.defineProperty(document, 'visibilityState', { get: () => 'visible', configurable: true });
                        const origMatch = window.matchMedia;
                        if (origMatch) {
                            window.matchMedia = function(q) {
                                if (typeof q === 'string' && q.includes('prefers-reduced-motion')) {
                                    return {
                                        matches: false,
                                        media: q,
                                        onchange: null,
                                        addListener: function() {},
                                        removeListener: function() {},
                                        addEventListener: function() {},
                                        removeEventListener: function() {},
                                        dispatchEvent: function() { return false; }
                                    };
                                }
                                return origMatch.call(window, q);
                            };
                        }
                    } catch(e) {}
                ");
            }
            catch {}

            if (!string.IsNullOrWhiteSpace(TabletWebView.CoreWebView2.Settings.UserAgent))
            {
                _onDutyService.UpdateUserAgent(TabletWebView.CoreWebView2.Settings.UserAgent);
            }

            _webViewInitialized = true;
            LoadingIndicator.Visibility = Visibility.Collapsed;

            // Wire up webview events
            TabletWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            TabletWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            TabletWebView.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
            TabletWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            try
            {
                TabletWebView.CoreWebView2.AddWebResourceRequestedFilter("https://md.b-zone.ro/api/*", Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);
                TabletWebView.CoreWebView2.WebResourceResponseReceived += CoreWebView2_WebResourceResponseReceived;
            }
            catch {}

            // Check connection and refresh Duty & Bandages as soon as browser engine is ready
            _ = CheckAndRefreshDutyAsync(showLoading: false);

            // Apply default zoom
            TabletWebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            ApplyZoom();

            // Navigate to requested destination URL
            string finalDest = !string.IsNullOrWhiteSpace(_pendingUrl) ? _pendingUrl : dest;
            if (!finalDest.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !finalDest.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                finalDest = "https://" + finalDest;
            }

            TabletWebView.Source = new Uri(finalDest);
            UrlTextBox.Text = finalDest;
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

        // When user navigates or logs into md.b-zone.ro, auto-refresh duty with new session cookies!
        if (TabletWebView.Source != null && TabletWebView.Source.Host.Contains("b-zone.ro", StringComparison.OrdinalIgnoreCase))
        {
            _ = CheckAndRefreshDutyAsync(showLoading: false);
        }
    }

    private void CoreWebView2_WebResourceResponseReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            string uri = e.Request.Uri;
            if (uri.Contains("b-zone.ro", StringComparison.OrdinalIgnoreCase))
            {
                if (uri.Contains("/api/auth", StringComparison.OrdinalIgnoreCase) ||
                    uri.Contains("/api/duty", StringComparison.OrdinalIgnoreCase) ||
                    uri.Contains("/api/bandaje", StringComparison.OrdinalIgnoreCase) ||
                    uri.Contains("/api/profil", StringComparison.OrdinalIgnoreCase) ||
                    uri.Contains("/api/homepage", StringComparison.OrdinalIgnoreCase) ||
                    uri.Contains("/login", StringComparison.OrdinalIgnoreCase))
                {
                    _ = Dispatcher.InvokeAsync(() => CheckAndRefreshDutyAsync(showLoading: false));
                }
            }
        }
        catch {}
    }

    private void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        if (TabletWebView.Source != null)
        {
            UrlTextBox.Text = TabletWebView.Source.ToString();
        }
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!string.IsNullOrWhiteSpace(e.Uri))
        {
            NavigateTo(e.Uri);
        }
    }

    private void UpdateNavigationButtons()
    {
        if (_isInitialized && TabletWebView.CoreWebView2 != null)
        {
            BackBtn.IsEnabled = true;
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
            BottomBar.Visibility = Visibility.Visible;
            BottomBar.CornerRadius = new CornerRadius(0, 0, 8, 8);

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
            BottomBar.CornerRadius = new CornerRadius(0, 0, 18, 18);

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
            _ = CheckAndRefreshDutyAsync(showLoading: false);
        }
        if (_settings.TabletPlaySounds)
        {
            SoundService.PlayOverlayShow();
        }

        _ = CheckForUpdatesSilentlyAsync();
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
        else
        {
            CloseAllAppsAndGoHome();
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
            StopDutyAutoRefresh();
            _dutyToastTimer?.Stop();
            _keepAliveTimer?.Stop();
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
        else if (_images.Count > 0)
        {
            TabletImagesDataGrid.SelectedIndex = 0;
        }
        else
        {
            ClearTabletPropertiesPanel();
        }
    }

    private void PopulateTabletPropertiesPanel(OverlayImage image)
    {
        _isUpdatingImagePropertiesUi = true;
        try
        {
            TabletPropertiesPanel.IsEnabled = true;
            TabletNoSelectionText.Visibility = Visibility.Collapsed;
            
            TabletImageOpacitySlider.Value = image.Opacity;
            TabletImageOpacityValueText.Text = $"{(image.Opacity * 100):F0}%";
            
            double minX = Math.Min(-2500, image.X - 500);
            double maxX = Math.Max(3840, image.X + 1500);
            double minY = Math.Min(-1500, image.Y - 500);
            double maxY = Math.Max(2160, image.Y + 1500);
            
            if (TabletPosXSlider != null)
            {
                TabletPosXSlider.Minimum = minX;
                TabletPosXSlider.Maximum = maxX;
                TabletPosXSlider.Value = Math.Clamp(image.X, minX, maxX);
            }
            if (TabletPosYSlider != null)
            {
                TabletPosYSlider.Minimum = minY;
                TabletPosYSlider.Maximum = maxY;
                TabletPosYSlider.Value = Math.Clamp(image.Y, minY, maxY);
            }

            double maxDimension = Math.Max(3840, Math.Max(image.Width, image.Height) + 500);
            if (TabletImageWidthSlider != null)
            {
                TabletImageWidthSlider.Minimum = 30;
                TabletImageWidthSlider.Maximum = maxDimension;
                TabletImageWidthSlider.Value = Math.Clamp(image.Width, 30, TabletImageWidthSlider.Maximum);
            }
            if (TabletImageHeightSlider != null)
            {
                TabletImageHeightSlider.Minimum = 30;
                TabletImageHeightSlider.Maximum = maxDimension;
                TabletImageHeightSlider.Value = Math.Clamp(image.Height, 30, TabletImageHeightSlider.Maximum);
            }
            TabletPosXTextBox.Text = image.X.ToString("F0");
            TabletPosYTextBox.Text = image.Y.ToString("F0");
            TabletImageWidthTextBox.Text = image.Width.ToString("F0");
            TabletImageHeightTextBox.Text = image.Height.ToString("F0");
            
            TabletMaintainAspectCheckBox.IsChecked = image.MaintainAspectRatio;
            TabletLockedCheckBox.IsChecked = image.IsLocked;
            UpdateAspectVisibility(image.MaintainAspectRatio);
        }
        finally
        {
            _isUpdatingImagePropertiesUi = false;
        }
    }

    private void ClearTabletPropertiesPanel()
    {
        TabletPropertiesPanel.IsEnabled = false;
        TabletNoSelectionText.Text = "Nu există nicio imagine adăugată. Folosește butonul '➕ Adaugă Imagine'.";
        TabletNoSelectionText.Visibility = Visibility.Visible;
    }

    private void TabletImageOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingImagePropertiesUi) return;
        if (TabletImagesDataGrid.SelectedItem is OverlayImage image)
        {
            image.Opacity = e.NewValue;
            TabletImageOpacityValueText.Text = $"{(e.NewValue * 100):F0}%";
            _saveConfigCallback?.Invoke();
        }
    }

    private void TabletPosXSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingImagePropertiesUi) return;
        if (TabletImagesDataGrid.SelectedItem is not OverlayImage image) return;

        double val = Math.Round(e.NewValue);
        image.X = val;

        _isUpdatingImagePropertiesUi = true;
        try
        {
            TabletPosXTextBox.Text = val.ToString("F0");
        }
        finally
        {
            _isUpdatingImagePropertiesUi = false;
        }

        _saveConfigCallback?.Invoke();
    }

    private void TabletPosYSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingImagePropertiesUi) return;
        if (TabletImagesDataGrid.SelectedItem is not OverlayImage image) return;

        double val = Math.Round(e.NewValue);
        image.Y = val;

        _isUpdatingImagePropertiesUi = true;
        try
        {
            TabletPosYTextBox.Text = val.ToString("F0");
        }
        finally
        {
            _isUpdatingImagePropertiesUi = false;
        }

        _saveConfigCallback?.Invoke();
    }

    private void TabletPositionTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingImagePropertiesUi) return;
        if (TabletImagesDataGrid.SelectedItem is not OverlayImage image) return;
        
        bool changed = false;
        if (double.TryParse(TabletPosXTextBox.Text, out var x))
        {
            image.X = x;
            if (TabletPosXSlider != null)
            {
                if (x > TabletPosXSlider.Maximum) TabletPosXSlider.Maximum = x + 500;
                if (x < TabletPosXSlider.Minimum) TabletPosXSlider.Minimum = x - 500;
                _isUpdatingImagePropertiesUi = true;
                try
                {
                    TabletPosXSlider.Value = Math.Clamp(x, TabletPosXSlider.Minimum, TabletPosXSlider.Maximum);
                }
                finally
                {
                    _isUpdatingImagePropertiesUi = false;
                }
            }
            changed = true;
        }

        if (double.TryParse(TabletPosYTextBox.Text, out var y))
        {
            image.Y = y;
            if (TabletPosYSlider != null)
            {
                if (y > TabletPosYSlider.Maximum) TabletPosYSlider.Maximum = y + 500;
                if (y < TabletPosYSlider.Minimum) TabletPosYSlider.Minimum = y - 500;
                _isUpdatingImagePropertiesUi = true;
                try
                {
                    TabletPosYSlider.Value = Math.Clamp(y, TabletPosYSlider.Minimum, TabletPosYSlider.Maximum);
                }
                finally
                {
                    _isUpdatingImagePropertiesUi = false;
                }
            }
            changed = true;
        }
        
        if (changed)
        {
            _saveConfigCallback?.Invoke();
        }
    }

    private void UpdateAspectVisibility(bool maintainAspect)
    {
        if (TabletWidthLabel != null)
        {
            TabletWidthLabel.Text = maintainAspect ? "Dimensiune:" : "Lățime:";
        }
        if (TabletImageHeightGrid != null)
        {
            TabletImageHeightGrid.Visibility = maintainAspect ? Visibility.Collapsed : Visibility.Visible;
        }
        if (TabletDimensionSubtext != null)
        {
            if (maintainAspect && TabletImagesDataGrid.SelectedItem is OverlayImage img)
            {
                TabletDimensionSubtext.Text = $"Dimensiune: {img.Width:F0} × {img.Height:F0} px";
                TabletDimensionSubtext.Visibility = Visibility.Visible;
            }
            else
            {
                TabletDimensionSubtext.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void EnsureSizeSlidersMax(double requiredVal)
    {
        if (TabletImageWidthSlider != null && TabletImageHeightSlider != null)
        {
            double currentMax = Math.Max(TabletImageWidthSlider.Maximum, TabletImageHeightSlider.Maximum);
            if (requiredVal > currentMax)
            {
                double newMax = requiredVal + 500;
                TabletImageWidthSlider.Maximum = newMax;
                TabletImageHeightSlider.Maximum = newMax;
            }
        }
    }

    private void TabletImageWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingImagePropertiesUi) return;
        if (TabletImagesDataGrid.SelectedItem is not OverlayImage image) return;

        double newWidth = Math.Round(e.NewValue);
        if (newWidth <= 0) return;

        image.Width = newWidth;

        _isUpdatingImagePropertiesUi = true;
        try
        {
            TabletImageWidthTextBox.Text = newWidth.ToString("F0");

            if (image.MaintainAspectRatio)
            {
                double ratio = GetImageAspectRatio(image);
                if (ratio > 0)
                {
                    double newHeight = Math.Max(20, Math.Round(newWidth / ratio));
                    image.Height = newHeight;
                    if (TabletImageHeightSlider != null)
                    {
                        EnsureSizeSlidersMax(newHeight);
                        TabletImageHeightSlider.Value = Math.Clamp(newHeight, TabletImageHeightSlider.Minimum, TabletImageHeightSlider.Maximum);
                    }
                    TabletImageHeightTextBox.Text = newHeight.ToString("F0");
                }
            }
        }
        finally
        {
            _isUpdatingImagePropertiesUi = false;
        }

        _saveConfigCallback?.Invoke();
    }

    private void TabletImageHeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingImagePropertiesUi) return;
        if (TabletImagesDataGrid.SelectedItem is not OverlayImage image) return;

        double newHeight = Math.Round(e.NewValue);
        if (newHeight <= 0) return;

        image.Height = newHeight;

        _isUpdatingImagePropertiesUi = true;
        try
        {
            TabletImageHeightTextBox.Text = newHeight.ToString("F0");

            if (image.MaintainAspectRatio)
            {
                double ratio = GetImageAspectRatio(image);
                if (ratio > 0)
                {
                    double newWidth = Math.Max(20, Math.Round(newHeight * ratio));
                    image.Width = newWidth;
                    if (TabletImageWidthSlider != null)
                    {
                        EnsureSizeSlidersMax(newWidth);
                        TabletImageWidthSlider.Value = Math.Clamp(newWidth, TabletImageWidthSlider.Minimum, TabletImageWidthSlider.Maximum);
                    }
                    TabletImageWidthTextBox.Text = newWidth.ToString("F0");
                }
            }
        }
        finally
        {
            _isUpdatingImagePropertiesUi = false;
        }

        _saveConfigCallback?.Invoke();
    }

    private void TabletSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingImagePropertiesUi) return;
        if (TabletImagesDataGrid.SelectedItem is not OverlayImage image) return;
        
        bool changed = false;
        if (double.TryParse(TabletImageWidthTextBox.Text, out var width) && width > 0)
        {
            image.Width = width;
            if (TabletImageWidthSlider != null)
            {
                EnsureSizeSlidersMax(width);
                _isUpdatingImagePropertiesUi = true;
                try
                {
                    TabletImageWidthSlider.Value = Math.Clamp(width, TabletImageWidthSlider.Minimum, TabletImageWidthSlider.Maximum);
                }
                finally
                {
                    _isUpdatingImagePropertiesUi = false;
                }
            }
            changed = true;
        }

        if (double.TryParse(TabletImageHeightTextBox.Text, out var height) && height > 0)
        {
            image.Height = height;
            if (TabletImageHeightSlider != null)
            {
                EnsureSizeSlidersMax(height);
                _isUpdatingImagePropertiesUi = true;
                try
                {
                    TabletImageHeightSlider.Value = Math.Clamp(height, TabletImageHeightSlider.Minimum, TabletImageHeightSlider.Maximum);
                }
                finally
                {
                    _isUpdatingImagePropertiesUi = false;
                }
            }
            changed = true;
        }
        
        if (changed)
        {
            _saveConfigCallback?.Invoke();
        }
    }

    private double GetImageAspectRatio(OverlayImage image)
    {
        try
        {
            string absPath = image.GetAbsoluteFilePath();
            if (File.Exists(absPath))
            {
                using var stream = File.OpenRead(absPath);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder.Frames.Count > 0)
                {
                    var frame = decoder.Frames[0];
                    if (frame.PixelHeight > 0)
                    {
                        return frame.PixelWidth / (double)frame.PixelHeight;
                    }
                }
            }
        }
        catch {}

        if (image.Width > 0 && image.Height > 0)
        {
            return image.Width / image.Height;
        }

        return 1.0;
    }

    private void TabletResetSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TabletImagesDataGrid.SelectedItem is not OverlayImage image) return;
        try
        {
            string absPath = image.GetAbsoluteFilePath();
            if (File.Exists(absPath))
            {
                using var stream = File.OpenRead(absPath);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder.Frames.Count > 0)
                {
                    var frame = decoder.Frames[0];
                    if (frame.PixelWidth > 0 && frame.PixelHeight > 0)
                    {
                        image.Width = frame.PixelWidth;
                        image.Height = frame.PixelHeight;

                        _isUpdatingImagePropertiesUi = true;
                        try
                        {
                            if (TabletImageWidthSlider != null)
                            {
                                EnsureSizeSlidersMax(image.Width);
                                TabletImageWidthSlider.Value = Math.Clamp(image.Width, TabletImageWidthSlider.Minimum, TabletImageWidthSlider.Maximum);
                            }
                            if (TabletImageHeightSlider != null)
                            {
                                EnsureSizeSlidersMax(image.Height);
                                TabletImageHeightSlider.Value = Math.Clamp(image.Height, TabletImageHeightSlider.Minimum, TabletImageHeightSlider.Maximum);
                            }

                            TabletImageWidthTextBox.Text = image.Width.ToString("F0");
                            TabletImageHeightTextBox.Text = image.Height.ToString("F0");
                        }
                        finally
                        {
                            _isUpdatingImagePropertiesUi = false;
                        }

                        _saveConfigCallback?.Invoke();
                    }
                }
            }
        }
        catch {}
    }


    private void TabletMaintainAspectCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingImagePropertiesUi) return;
        if (TabletImagesDataGrid.SelectedItem is OverlayImage image)
        {
            image.MaintainAspectRatio = true;
            UpdateAspectVisibility(true);
            _saveConfigCallback?.Invoke();
        }
    }

    private void TabletMaintainAspectCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingImagePropertiesUi) return;
        if (TabletImagesDataGrid.SelectedItem is OverlayImage image)
        {
            image.MaintainAspectRatio = false;
            UpdateAspectVisibility(false);
            _saveConfigCallback?.Invoke();
        }
    }

    private void TabletLockedCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingImagePropertiesUi) return;
        if (TabletImagesDataGrid.SelectedItem is OverlayImage image)
        {
            image.IsLocked = true;
            _saveConfigCallback?.Invoke();
        }
    }

    private void TabletLockedCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingImagePropertiesUi) return;
        if (TabletImagesDataGrid.SelectedItem is OverlayImage image)
        {
            image.IsLocked = false;
            _saveConfigCallback?.Invoke();
        }
    }

    private void TabletRemoveImageBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TabletImagesDataGrid.SelectedItem is not OverlayImage selectedImage) return;

        _images.Remove(selectedImage);
        _saveConfigCallback?.Invoke();
        _refreshOverlaysCallback?.Invoke();
        if (_images.Count > 0)
        {
            TabletImagesDataGrid.SelectedIndex = 0;
        }
        else
        {
            ClearTabletPropertiesPanel();
        }
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

    private KeybindViewModel CreateKeybindViewModel(KeybindEntry entry)
    {
        var vm = new KeybindViewModel(entry);
        vm.PropertyChanged += KeybindViewModel_PropertyChanged;
        return vm;
    }

    private void KeybindViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isPopulatingKeybindPanel) return;
        if (sender is KeybindViewModel vm)
        {
            if (e.PropertyName == nameof(KeybindViewModel.Enabled))
            {
                if (vm.Enabled && RoleplayComplianceService.HasBlockedLines(vm.Entry.Script, out _, out _))
                {
                    vm.Enabled = false;
                    SoundService.PlayKeybindStop();
                }

                if (TabletKeybindsDataGrid.SelectedItem == vm)
                {
                    _isPopulatingKeybindPanel = true;
                    try
                    {
                        TabletKeybindEnabledCheckBox.IsChecked = vm.Enabled;
                    }
                    finally
                    {
                        _isPopulatingKeybindPanel = false;
                    }
                }

                _saveConfigCallback?.Invoke();
                _refreshKeybindsCallback?.Invoke();
            }
        }
    }

    private void TabletKeybindsDataGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _saveConfigCallback?.Invoke();
            _refreshKeybindsCallback?.Invoke();
        });
    }

    private void TabletKeybindGridCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is KeybindViewModel vm)
        {
            if (vm.Enabled && RoleplayComplianceService.HasBlockedLines(vm.Entry.Script, out _, out _))
            {
                vm.Enabled = false;
                cb.IsChecked = false;
                SoundService.PlayKeybindStop();
            }

            if (TabletKeybindsDataGrid.SelectedItem == vm)
            {
                _isPopulatingKeybindPanel = true;
                try
                {
                    TabletKeybindEnabledCheckBox.IsChecked = vm.Enabled;
                }
                finally
                {
                    _isPopulatingKeybindPanel = false;
                }
            }
            else
            {
                TabletKeybindsDataGrid.SelectedItem = vm;
            }

            _saveConfigCallback?.Invoke();
            _refreshKeybindsCallback?.Invoke();
        }
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
        var vm = CreateKeybindViewModel(entry);
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
            ValidateKeybindScriptAndSetUI(vm.Entry.Script, vm);
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
            if (TabletKeybindScriptWarningBorder != null) TabletKeybindScriptWarningBorder.Visibility = Visibility.Collapsed;
            TabletKeybindScriptTextBox.ClearValue(Border.BorderBrushProperty);
            TabletKeybindEnabledCheckBox.IsEnabled = true;
            TabletKeybindEnabledCheckBox.ToolTip = null;
            TabletKeybindEnabledCheckBox.IsChecked = false;
        }
        finally
        {
            _isPopulatingKeybindPanel = false;
        }
    }

    private void ValidateKeybindScriptAndSetUI(string script, KeybindViewModel? vm)
    {
        if (TabletKeybindScriptWarningBorder == null) return;

        if (RoleplayComplianceService.HasBlockedLines(script, out string? blockedLine, out string? matchedPhrase))
        {
            TabletKeybindScriptWarningBorder.Visibility = Visibility.Visible;
            TabletKeybindScriptWarningText.Text = RoleplayComplianceService.GetWarningMessage(blockedLine ?? "", matchedPhrase ?? "");
            TabletKeybindScriptTextBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));

            if (vm != null && vm.Enabled)
            {
                vm.Enabled = false;
                TabletKeybindEnabledCheckBox.IsChecked = false;
            }
            TabletKeybindEnabledCheckBox.IsEnabled = false;
            TabletKeybindEnabledCheckBox.ToolTip = "Acest keybind conține comenzi blocate de Regulamentul Serverului (2.27 LER) și nu poate fi activat.";
        }
        else
        {
            TabletKeybindScriptWarningBorder.Visibility = Visibility.Collapsed;
            TabletKeybindScriptTextBox.ClearValue(Border.BorderBrushProperty);
            TabletKeybindEnabledCheckBox.IsEnabled = true;
            TabletKeybindEnabledCheckBox.ToolTip = null;
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
            ValidateKeybindScriptAndSetUI(TabletKeybindScriptTextBox.Text, vm);
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
            if (RoleplayComplianceService.HasBlockedLines(vm.Entry.Script, out _, out _))
            {
                TabletKeybindEnabledCheckBox.IsChecked = false;
                SoundService.PlayKeybindStop();
                return;
            }

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
        if (WebTabContent != null && WebTabContent.Visibility == Visibility.Visible)
        {
            _wasWebViewVisibleBeforeLock = true;
            if (TabletWebView != null) TabletWebView.Visibility = Visibility.Hidden;
        }
        else
        {
            _wasWebViewVisibleBeforeLock = false;
        }

        if (LockscreenGreetingText != null)
        {
            LockscreenGreetingText.Text = GetTimeBasedGreeting(DateTime.Now);
        }

        LockscreenGrid.Visibility = Visibility.Visible;
        StopDutyAutoRefresh();
    }

    private static string GetTimeBasedGreeting(DateTime now)
    {
        int hour = now.Hour;
        if (hour >= 5 && hour < 12)
        {
            return "Bună dimineața, spor la muncă!";
        }
        else if (hour >= 12 && hour < 18)
        {
            return "Bună ziua, spor la muncă!";
        }
        else
        {
            return "Bună seara, spor la muncă!";
        }
    }

    private void UnlockTablet()
    {
        LockscreenGrid.Visibility = Visibility.Collapsed;

        if (_wasWebViewVisibleBeforeLock)
        {
            if (TabletWebView != null) TabletWebView.Visibility = Visibility.Visible;
            _wasWebViewVisibleBeforeLock = false;
        }

        if (DutySidebarBorder != null && DutySidebarBorder.Visibility == Visibility.Visible)
        {
            StartDutyAutoRefresh();
            _ = CheckAndRefreshDutyAsync(showLoading: false);
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
        if (appGrid == WebTabContent)
        {
            if (TabletWebView != null) TabletWebView.Visibility = Visibility.Visible;
            _wasWebViewVisibleBeforeModal = false;
            _wasWebViewVisibleBeforeLock = false;
        }

        DesktopGrid.Visibility = Visibility.Collapsed;
        WebTabContent.Visibility = Visibility.Collapsed;
        ToolsTabContent.Visibility = Visibility.Collapsed;

        SettingsTabContent.Visibility = Visibility.Collapsed;

        appGrid.Visibility = Visibility.Visible;
        NavToolbarBorder.Visibility = showBrowserToolbar ? Visibility.Visible : Visibility.Collapsed;
        TitleBarHomeBtn.Visibility = showBrowserToolbar ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CloseAllAppsAndGoHome()
    {
        if (UpdateModalGrid != null && UpdateModalGrid.Visibility == Visibility.Visible)
        {
            CancelUpdateBtn_Click(this, new RoutedEventArgs());
        }

        if (_wasWebViewVisibleBeforeModal)
        {
            if (TabletWebView != null) TabletWebView.Visibility = Visibility.Visible;
            _wasWebViewVisibleBeforeModal = false;
        }

        if (_wasWebViewVisibleBeforeLock)
        {
            if (TabletWebView != null) TabletWebView.Visibility = Visibility.Visible;
            _wasWebViewVisibleBeforeLock = false;
        }

        WebTabContent.Visibility = Visibility.Collapsed;
        ToolsTabContent.Visibility = Visibility.Collapsed;

        SettingsTabContent.Visibility = Visibility.Collapsed;
        NavToolbarBorder.Visibility = Visibility.Collapsed;
        TitleBarHomeBtn.Visibility = Visibility.Collapsed;

        DesktopGrid.Visibility = Visibility.Visible;
    }

    private void EnsureWebViewInitialized(string? targetUrl = null)
    {
        string dest = !string.IsNullOrWhiteSpace(targetUrl)
            ? targetUrl
            : (!string.IsNullOrWhiteSpace(UrlTextBox.Text) ? UrlTextBox.Text : _settings.TabletUrl);

        _pendingUrl = dest;
        UrlTextBox.Text = dest;
        BackBtn.IsEnabled = true;

        if (!_webViewInitialized)
        {
            _webViewInitialized = true;
            InitializeWebView(dest);
        }
        else
        {
            NavigateTo(dest);
        }
    }

    private void BrowserMdApp_Click(object sender, RoutedEventArgs e)
    {
        UrlTextBox.Text = "https://md.b-zone.ro";
        OpenApp(WebTabContent, true);
        EnsureWebViewInitialized("https://md.b-zone.ro");
    }

    private void RpgPanelApp_Click(object sender, RoutedEventArgs e)
    {
        UrlTextBox.Text = "https://gta5.ro";
        OpenApp(WebTabContent, true);
        EnsureWebViewInitialized("https://gta5.ro");
    }

    private void MaiApp_Click(object sender, RoutedEventArgs e)
    {
        UrlTextBox.Text = "https://mai.b-zone.ro";
        OpenApp(WebTabContent, true);
        EnsureWebViewInitialized("https://mai.b-zone.ro");
    }

    private void GovApp_Click(object sender, RoutedEventArgs e)
    {
        UrlTextBox.Text = "https://gov.b-zone.ro/";
        OpenApp(WebTabContent, true);
        EnsureWebViewInitialized("https://gov.b-zone.ro/");
    }

    public void OpenTools(string targetTab = "Keybinds")
    {
        OpenApp(ToolsTabContent, false);
        SetToolsNavigation(targetTab);
    }

    private void ToolsApp_Click(object sender, RoutedEventArgs e)
    {
        OpenTools("Keybinds");
    }

    private void ToolsNavKeybindsBtn_Click(object sender, RoutedEventArgs e)
    {
        SetToolsNavigation("Keybinds");
    }

    private void ToolsNavOverlaysBtn_Click(object sender, RoutedEventArgs e)
    {
        SetToolsNavigation("Overlays");
    }

    private void SetToolsNavigation(string tab)
    {
        bool isKeybinds = tab == "Keybinds";

        if (ToolsKeybindsView != null)
            ToolsKeybindsView.Visibility = isKeybinds ? Visibility.Visible : Visibility.Collapsed;

        if (ToolsOverlaysView != null)
            ToolsOverlaysView.Visibility = isKeybinds ? Visibility.Collapsed : Visibility.Visible;

        if (ToolsNavKeybindsBtn != null)
        {
            ToolsNavKeybindsBtn.Background = isKeybinds ? new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0xE5, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF));
            ToolsNavKeybindsBtn.BorderBrush = isKeybinds ? new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xE5, 0xFF)) : Brushes.Transparent;
            ToolsNavKeybindsBtn.BorderThickness = isKeybinds ? new Thickness(1) : new Thickness(0);
            ToolsNavKeybindsBtn.Foreground = isKeybinds ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
        }

        if (ToolsNavOverlaysBtn != null)
        {
            ToolsNavOverlaysBtn.Background = !isKeybinds ? new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0xE5, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF));
            ToolsNavOverlaysBtn.BorderBrush = !isKeybinds ? new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xE5, 0xFF)) : Brushes.Transparent;
            ToolsNavOverlaysBtn.BorderThickness = !isKeybinds ? new Thickness(1) : new Thickness(0);
            ToolsNavOverlaysBtn.Foreground = !isKeybinds ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
        }

        if (isKeybinds)
        {
            if (TabletKeybindsDataGrid != null && TabletKeybindsDataGrid.SelectedIndex == -1 && _tabletKeybinds.Count > 0)
            {
                TabletKeybindsDataGrid.SelectedIndex = 0;
            }
        }
        else
        {
            if (TabletImagesDataGrid != null && TabletImagesDataGrid.SelectedIndex == -1 && _images.Count > 0)
            {
                TabletImagesDataGrid.SelectedIndex = 0;
            }
        }
    }

    private void OverlaysApp_Click(object sender, RoutedEventArgs e) => OpenTools("Overlays");
    private void KeybindsApp_Click(object sender, RoutedEventArgs e) => OpenTools("Keybinds");
    private void SettingsApp_Click(object sender, RoutedEventArgs e)
    {
        OpenApp(SettingsTabContent, false);
    }

    // ==========================================
    // ON-DUTY MEDICS DISPATCH SYSTEM (SIDEBAR)
    // ==========================================


    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private void CenterWindowOnCurrentMonitor()
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
            {
                IntPtr hMonitor = MonitorFromWindow(helper.Handle, MONITOR_DEFAULTTONEAREST);
                if (hMonitor != IntPtr.Zero)
                {
                    var mi = new MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    if (GetMonitorInfo(hMonitor, ref mi))
                    {
                        var source = PresentationSource.FromVisual(this);
                        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                        double workLeft = mi.rcWork.Left / dpiX;
                        double workTop = mi.rcWork.Top / dpiY;
                        double workWidth = (mi.rcWork.Right - mi.rcWork.Left) / dpiX;
                        double workHeight = (mi.rcWork.Bottom - mi.rcWork.Top) / dpiY;

                        Left = workLeft + (workWidth - Width) / 2.0;
                        Top = workTop + (workHeight - Height) / 2.0;
                        return;
                    }
                }
            }
        }
        catch { }

        // Fallback using SystemParameters.WorkArea
        Left = (SystemParameters.WorkArea.Width - Width) / 2.0;
        Top = (SystemParameters.WorkArea.Height - Height) / 2.0;
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
            CenterWindowOnCurrentMonitor();
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
            Width += DutySidebarTotalWidth;
            DutySidebarCol.Width = new GridLength(DutySidebarTotalWidth);
            DutySidebarBorder.Visibility = Visibility.Visible;
            CenterWindowOnCurrentMonitor();
            if (DutyExpandTabBtn != null) DutyExpandTabBtn.Visibility = Visibility.Collapsed;

            StartDutyAutoRefresh();
            _ = CheckAndRefreshDutyAsync(showLoading: _currentDutyResult == null);
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
            await CheckAndRefreshDutyAsync(showLoading: true);
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
                    await CheckAndRefreshDutyAsync(showLoading: false);
                }
            };
        }
        _onDutyRefreshTimer.Start();
    }

    private void StopDutyAutoRefresh()
    {
        _onDutyRefreshTimer?.Stop();
    }

    private void UpdateDutyTitleBarButton(string text, string colorHex, double opacity, string tooltip)
    {
        if (DutyTitleBarToggleText != null)
        {
            DutyTitleBarToggleText.Text = text;
        }
        if (DutyTitleBarStatusDot != null)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorHex);
                DutyTitleBarStatusDot.Fill = new SolidColorBrush(color);
                if (DutyTitleBarStatusDot.Effect is DropShadowEffect dropShadow)
                {
                    dropShadow.Color = color;
                }
            }
            catch {}
        }
        if (DutyTitleBarToggleBtn != null)
        {
            DutyTitleBarToggleBtn.Opacity = opacity;
            DutyTitleBarToggleBtn.ToolTip = tooltip;
        }
    }

    private void SetBandagesOfflineUi()
    {
        if (BandagesActiveContentGrid != null) BandagesActiveContentGrid.Visibility = Visibility.Collapsed;
        if (BandagesGameOfflineState != null) BandagesGameOfflineState.Visibility = Visibility.Visible;
    }

    private async void KeepAliveTimer_Tick(object? sender, EventArgs e)
    {
        if (!_settings.PanelSyncConsentGiven) return;
        try
        {
            string? cookieHeader = await GetPanelCookieHeaderAsync();
            if (!string.IsNullOrWhiteSpace(cookieHeader))
            {
                await _onDutyService.SendKeepAlivePingAsync(cookieHeader);
            }
        }
        catch
        {
            // Ignore background keepalive errors
        }
    }

    private Task HandleSetCookiesFromApiAsync(IEnumerable<string> setCookies)
    {
        try
        {
            if (TabletWebView?.CoreWebView2 == null) return Task.CompletedTask;

            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (TabletWebView?.CoreWebView2 == null) return;
                    var cookieManager = TabletWebView.CoreWebView2.CookieManager;

                    foreach (var cookieHeader in setCookies)
                    {
                        var parts = cookieHeader.Split(';');
                        if (parts.Length == 0) continue;
                        var kv = parts[0].Split('=', 2);
                        if (kv.Length != 2) continue;
                        string name = kv[0].Trim();
                        string value = kv[1].Trim();
                        string domain = ".b-zone.ro";
                        string path = "/";

                        for (int i = 1; i < parts.Length; i++)
                        {
                            var p = parts[i].Trim();
                            if (p.StartsWith("Domain=", StringComparison.OrdinalIgnoreCase)) domain = p.Substring(7).Trim();
                            else if (p.StartsWith("Path=", StringComparison.OrdinalIgnoreCase)) path = p.Substring(5).Trim();
                        }

                        var cookie = cookieManager.CreateCookie(name, value, domain, path);
                        cookieManager.AddOrUpdateCookie(cookie);
                    }
                }
                catch {}
            });
        }
        catch {}
        return Task.CompletedTask;
    }

    private async Task<string?> GetPanelCookieHeaderAsync()
    {
        try
        {
            if (TabletWebView != null)
            {
                if (TabletWebView.CoreWebView2 == null)
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string userFolder = System.IO.Path.Combine(appData, "MDTab-Helper", "TabletCache");
                    var envOptions = new CoreWebView2EnvironmentOptions
                    {
                        AdditionalBrowserArguments = "--disable-background-timer-throttling --disable-backgrounding-occluded-windows --disable-renderer-backgrounding --autoplay-policy=no-user-gesture-required --enable-gpu-rasterization --enable-zero-copy --ignore-gpu-blocklist"
                    };
                    var env = await CoreWebView2Environment.CreateAsync(null, userFolder, envOptions);
                    await TabletWebView.EnsureCoreWebView2Async(env);
                }

                if (TabletWebView.CoreWebView2 != null)
                {
                    var cookieManager = TabletWebView.CoreWebView2.CookieManager;

                    // Query cookies matching root (with trailing slash), API endpoint, and full b-zone jar
                    var rootCookies = await cookieManager.GetCookiesAsync("https://md.b-zone.ro/") ?? new List<CoreWebView2Cookie>();
                    var apiCookies = await cookieManager.GetCookiesAsync("https://md.b-zone.ro/api/duty/live") ?? new List<CoreWebView2Cookie>();
                    var allCookies = await cookieManager.GetCookiesAsync("") ?? new List<CoreWebView2Cookie>();

                    var bzoneCookies = rootCookies
                        .Concat(apiCookies)
                        .Concat(allCookies.Where(c => c.Domain.Contains("b-zone.ro", StringComparison.OrdinalIgnoreCase)))
                        .GroupBy(c => c.Name)
                        .Select(g => g.First())
                        .ToList();

                    if (bzoneCookies.Count > 0)
                    {
                        return string.Join("; ", bzoneCookies.Select(c => $"{c.Name}={c.Value}"));
                    }
                }
            }
        }
        catch
        {
            // CoreWebView2 not ready
        }
        return null;
    }

    private void UpdateBandagesUi(BandagesStockInfo bandages)
    {
        if (BandagesGameOfflineState != null) BandagesGameOfflineState.Visibility = Visibility.Collapsed;
        if (BandagesActiveContentGrid != null) BandagesActiveContentGrid.Visibility = Visibility.Visible;

        if (!bandages.IsSuccess) return;

        if (BandagesCountText != null)
        {
            BandagesCountText.Text = bandages.StockCount;
            BandagesCountText.ToolTip = $"Stoc Disponibil: {bandages.StockCount} buc\nPreț: {bandages.PricePerUnit}\nValoare Totală: {bandages.TotalValue}\nActualizat: {bandages.LastModified}";
        }
    }

    private async Task CheckAndRefreshDutyAsync(bool showLoading = true)
    {
        await RefreshOnDutyDataAsync(showLoading);
    }

    private async Task RefreshOnDutyDataAsync(bool showLoading = true)
    {
        if (_isRefreshingDuty) return;
        _isRefreshingDuty = true;
        try
        {
        // 1. If consent not yet granted, show the activation prompt in the sidebar card
        if (!_settings.PanelSyncConsentGiven)
        {
            if (DutyLoadingIndicator != null) DutyLoadingIndicator.Visibility = Visibility.Collapsed;
            if (DutyErrorState != null) DutyErrorState.Visibility = Visibility.Collapsed;
            if (DutyEmptyState != null) DutyEmptyState.Visibility = Visibility.Collapsed;
            if (DutyGameOfflineState != null) DutyGameOfflineState.Visibility = Visibility.Visible;
            if (DutyGroupsItemsControl != null) DutyGroupsItemsControl.ItemsSource = null;
            if (DutySidebarTotalBadge != null) DutySidebarTotalBadge.Text = "(Inactiv)";
            if (DutyLastUpdatedText != null) DutyLastUpdatedText.Text = "Sincronizare neactivată";
            if (DutyOfflineTitleText != null) DutyOfflineTitleText.Text = "Sincronizare Portal MD";
            if (DutyOfflineDescText != null) DutyOfflineDescText.Text = "Pentru a vedea medicii activi pe tură și stocul de bandaje, este necesară autorizarea sincronizării cu portalul.";
            if (DutyActivateSyncBtn != null)
            {
                DutyActivateSyncBtn.Content = "⚡ Activează Sincronizarea";
                DutyActivateSyncBtn.Visibility = Visibility.Visible;
            }
            if (DutyOfflineSubtext != null) DutyOfflineSubtext.Text = "Apasă pe butonul de mai sus pentru detalii și activare.";
            SetBandagesOfflineUi();
            UpdateDutyTitleBarButton("Medici Duty (Inactiv)", "#94A3B8", 0.85, "Medici Duty (Apasă pentru autorizare)");
            return;
        }

        // 2. Consent granted: get panel cookies
        string? cookieHeader = await GetPanelCookieHeaderAsync();

        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            if (DutyLoadingIndicator != null) DutyLoadingIndicator.Visibility = Visibility.Collapsed;
            if (DutyErrorState != null) DutyErrorState.Visibility = Visibility.Collapsed;
            if (DutyEmptyState != null) DutyEmptyState.Visibility = Visibility.Collapsed;
            if (DutyGameOfflineState != null) DutyGameOfflineState.Visibility = Visibility.Visible;
            if (DutyGroupsItemsControl != null) DutyGroupsItemsControl.ItemsSource = null;
            if (DutySidebarTotalBadge != null) DutySidebarTotalBadge.Text = "(Neconectat)";
            if (DutyLastUpdatedText != null) DutyLastUpdatedText.Text = "Neconectat la portal";
            if (DutyOfflineTitleText != null) DutyOfflineTitleText.Text = "Neconectat la Portal";
            if (DutyOfflineDescText != null) DutyOfflineDescText.Text = "Trebuie să fii conectat pe portalul MD în tabletă pentru a vedea personalul de tură.";
            if (DutyActivateSyncBtn != null)
            {
                DutyActivateSyncBtn.Content = "🌐 Deschide Portalul MD";
                DutyActivateSyncBtn.Visibility = Visibility.Visible;
            }
            if (DutyOfflineSubtext != null) DutyOfflineSubtext.Text = "Autentifică-te pe md.b-zone.ro în browserul tabletei.";
            SetBandagesOfflineUi();
            UpdateDutyTitleBarButton("Medici Duty (Neconectat)", "#F59E0B", 0.85, "Medici Duty (Necesită conectare pe portal)");
            return;
        }

        if (DutyGameOfflineState != null) DutyGameOfflineState.Visibility = Visibility.Collapsed;
        if (BandagesGameOfflineState != null) BandagesGameOfflineState.Visibility = Visibility.Collapsed;
        if (BandagesActiveContentGrid != null) BandagesActiveContentGrid.Visibility = Visibility.Visible;

        if (showLoading && _currentDutyResult == null && DutyLoadingIndicator != null)
        {
            DutyLoadingIndicator.Visibility = Visibility.Visible;
        }

        if (DutyErrorState != null) DutyErrorState.Visibility = Visibility.Collapsed;

        var result = await _onDutyService.FetchOnDutyDataAsync(cookieHeader);
        if (DutyLoadingIndicator != null) DutyLoadingIndicator.Visibility = Visibility.Collapsed;

        UpdateBandagesUi(result.Bandages);

        if (result.IsSuccess)
        {
            _currentDutyResult = result;
            if (DutyLastUpdatedText != null) DutyLastUpdatedText.Text = $"Sincronizat: {result.LastUpdated:HH:mm:ss}";
            if (DutySidebarTotalBadge != null) DutySidebarTotalBadge.Text = $"({result.TotalCount})";
            UpdateDutyTitleBarButton($"{result.TotalCount} Medici Duty", "#22C55E", 1.0, "Comută afișarea listei laterale de Medici la Datorie");

            ApplyDutyFilterAndSearch();
        }
        else
        {
            if (result.IsUnauthorized)
            {
                if (DutyGameOfflineState != null) DutyGameOfflineState.Visibility = Visibility.Visible;
                if (DutyGroupsItemsControl != null) DutyGroupsItemsControl.ItemsSource = null;
                if (DutySidebarTotalBadge != null) DutySidebarTotalBadge.Text = "(Neconectat)";
                if (DutyLastUpdatedText != null) DutyLastUpdatedText.Text = "Sesiune expirată";
                if (DutyOfflineTitleText != null) DutyOfflineTitleText.Text = "Sesiune Expirată";
                if (DutyOfflineDescText != null) DutyOfflineDescText.Text = "Sesiunea ta pe portal a expirat. Te rugăm să te reconectezi.";
                if (DutyActivateSyncBtn != null)
                {
                    DutyActivateSyncBtn.Content = "🌐 Reconectare Portal MD";
                    DutyActivateSyncBtn.Visibility = Visibility.Visible;
                }
                if (DutyOfflineSubtext != null) DutyOfflineSubtext.Text = "Apasă pe butonul de mai sus pentru a deschide pagina de login.";
                SetBandagesOfflineUi();
                UpdateDutyTitleBarButton("Medici Duty (Neconectat)", "#F59E0B", 0.85, "Medici Duty (Necesită conectare pe portal)");
            }
            else if (_currentDutyResult == null)
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
        finally
        {
            _isRefreshingDuty = false;
        }
    }

    private async void DutyActivateSyncBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.PanelSyncConsentGiven)
        {
            ShowPanelSyncConsentModal();
        }
        else
        {
            OpenApp(WebTabContent, true);
            EnsureWebViewInitialized("https://md.b-zone.ro");
            try
            {
                if (TabletWebView?.CoreWebView2 != null)
                {
                    await Task.Delay(400);
                    await TabletWebView.CoreWebView2.ExecuteScriptAsync("try { window.dispatchEvent(new CustomEvent('open-visitor-hub')); } catch(e) {}");
                }
            }
            catch {}
            _ = CheckAndRefreshDutyAsync(showLoading: true);
        }
    }

    private void ShowPanelSyncConsentModal()
    {
        if (PanelSyncConsentModalGrid == null) return;

        if (WebTabContent != null && WebTabContent.Visibility == Visibility.Visible && TabletWebView != null && TabletWebView.Visibility == Visibility.Visible)
        {
            _wasWebViewVisibleBeforeModal = true;
            TabletWebView.Visibility = Visibility.Hidden;
        }

        PanelSyncConsentModalGrid.Visibility = Visibility.Visible;
    }

    private void HidePanelSyncConsentModal()
    {
        if (PanelSyncConsentModalGrid == null) return;
        PanelSyncConsentModalGrid.Visibility = Visibility.Collapsed;

        if (_wasWebViewVisibleBeforeModal)
        {
            if (TabletWebView != null) TabletWebView.Visibility = Visibility.Visible;
            _wasWebViewVisibleBeforeModal = false;
        }
    }

    private void PanelSyncConsentCancel_Click(object sender, RoutedEventArgs e)
    {
        HidePanelSyncConsentModal();
    }

    private async void PanelSyncConsentConfirm_Click(object sender, RoutedEventArgs e)
    {
        _settings.PanelSyncConsentGiven = true;
        _saveConfigCallback?.Invoke();
        HidePanelSyncConsentModal();

        await CheckAndRefreshDutyAsync(showLoading: true);
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
            // Rank filter check (Asistent - Director)
            if (filter != "Toți")
            {
                if ((filter == "Directori" || filter == "Director") && !group.Grad.Contains("Director", StringComparison.OrdinalIgnoreCase)) continue;
                if ((filter == "Inspectori" || filter == "Medici Inspectori") && !group.Grad.Contains("Inspector", StringComparison.OrdinalIgnoreCase)) continue;
                if ((filter == "Primari" || filter == "Medici Primari") && !group.Grad.Contains("Primar", StringComparison.OrdinalIgnoreCase)) continue;
                if ((filter == "Specialiști" || filter == "Medici Specialiști") && !group.Grad.Contains("Speciali", StringComparison.OrdinalIgnoreCase)) continue;
                if ((filter == "Rezidenți" || filter == "Medici Rezidenți") && !group.Grad.Contains("Reziden", StringComparison.OrdinalIgnoreCase)) continue;
                if ((filter == "Stagiari" || filter == "Medici Stagiari") && !group.Grad.Contains("Stagiar", StringComparison.OrdinalIgnoreCase)) continue;
                if ((filter == "Asistenți" || filter == "Asistenți Medicali") && !group.Grad.Contains("Asisten", StringComparison.OrdinalIgnoreCase)) continue;
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

    private void DutyDoctorRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is OnDutyDoctor doc)
        {
            try
            {
                Clipboard.SetText(doc.Nume);
                ShowDutyCopyToast($"Copiat: {doc.Nume}");
            }
            catch
            {
                // Clipboard may be temporarily locked by another process
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
            _tabletKeybinds.Add(CreateKeybindViewModel(entry));
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

        dest.TabletEnabled = source.TabletEnabled;
        dest.TabletUrl = source.TabletUrl;
        dest.TabletVirtualKey = source.TabletVirtualKey;
        dest.TabletModifiers = source.TabletModifiers;
        dest.TabletOpacity = source.TabletOpacity;
        dest.TabletTheme = source.TabletTheme;
        dest.TabletPlaySounds = source.TabletPlaySounds;
        dest.TabletZoom = source.TabletZoom;
        dest.TabletWidth = source.TabletWidth;
        dest.TabletHeight = source.TabletHeight;
        dest.PanelSyncConsentGiven = source.PanelSyncConsentGiven;

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

    private DateTime _lastUpdateCheckTime = DateTime.MinValue;

    private async Task CheckForUpdatesSilentlyAsync(bool force = false)
    {
        if (!force && (DateTime.UtcNow - _lastUpdateCheckTime).TotalSeconds < 30)
        {
            return;
        }
        _lastUpdateCheckTime = DateTime.UtcNow;

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
        if (WebTabContent != null && WebTabContent.Visibility == Visibility.Visible)
        {
            _wasWebViewVisibleBeforeModal = true;
            if (TabletWebView != null) TabletWebView.Visibility = Visibility.Hidden;
        }
        else
        {
            _wasWebViewVisibleBeforeModal = false;
        }

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

        if (_wasWebViewVisibleBeforeModal)
        {
            if (TabletWebView != null) TabletWebView.Visibility = Visibility.Visible;
            _wasWebViewVisibleBeforeModal = false;
        }
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
