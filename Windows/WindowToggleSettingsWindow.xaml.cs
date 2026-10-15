using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using MDHelper.Models;
using MDHelper.Services;

namespace MDHelper.Windows;

public partial class WindowToggleSettingsWindow : Window
{
    private int _windowToggleVirtualKey = 0x68; // default Numpad8
    private uint _windowToggleModifiers = 2;    // default Ctrl
    private bool _isRecordingWindowToggle = false;

    private int _overlayToggleVirtualKey = 0x67; // default Numpad7
    private uint _overlayToggleModifiers = 2;    // default Ctrl
    private bool _isRecordingOverlayToggle = false;

    private int _tabletVirtualKey = 0x75; // default F6
    private uint _tabletModifiers = 0;    // default None
    private bool _isRecordingTablet = false;

    private int _copilotVirtualKey = 0x57; // default W
    private bool _isRecordingCopilotKey = false;

    private int _copilotTriggerVirtualKey = 0x11; // default Ctrl
    private bool _isRecordingCopilotTrigger = false;

    public KeybindSettings Settings { get; private set; }

    public WindowToggleSettingsWindow(KeybindSettings settings)
    {
        InitializeComponent();
        Settings = settings ?? new KeybindSettings();

        // Load Window Toggle values
        var wt = Settings.WindowToggle ?? new WindowToggleSettings();
        EnabledCheckBox.IsChecked = wt.Enabled;
        TitleComboBox.Text = wt.WindowTitle;
        XTextBox.Text = wt.X.ToString();
        YTextBox.Text = wt.Y.ToString();
        WidthTextBox.Text = wt.Width.ToString();
        HeightTextBox.Text = wt.Height.ToString();

        _windowToggleVirtualKey = wt.HotkeyVirtualKey;
        _windowToggleModifiers = wt.HotkeyModifiers;

        // Load Overlay Toggle values
        OverlayEnabledCheckBox.IsChecked = Settings.OverlayToggleEnabled;
        _overlayToggleVirtualKey = Settings.OverlayToggleVirtualKey;
        _overlayToggleModifiers = Settings.OverlayToggleModifiers;

        // Load Tablet Overlay values
        TabletEnabledCheckBox.IsChecked = Settings.TabletEnabled;
        TabletUrlTextBox.Text = Settings.TabletUrl;
        _tabletVirtualKey = Settings.TabletVirtualKey;
        _tabletModifiers = Settings.TabletModifiers;

        // Load System settings
        bool isStartup = StartupService.IsStartupEnabled();
        Settings.StartWithWindows = isStartup;
        StartWithWindowsCheckBox.IsChecked = isStartup;

        // Load Copilot settings
        CopilotEnabledCheckBox.IsChecked = Settings.CopilotEnabled;
        _copilotVirtualKey = Settings.CopilotVirtualKey;
        _copilotTriggerVirtualKey = Settings.CopilotTriggerVirtualKey;

        UpdateWindowToggleKeyDisplay();
        UpdateOverlayToggleKeyDisplay();
        UpdateTabletKeyDisplay();
        UpdateCopilotKeyDisplay();
        UpdateCopilotTriggerDisplay();
    }

    private void RecordKeyBtn_Click(object sender, RoutedEventArgs e)
    {
        ResetAllRecordingStates();
        _isRecordingWindowToggle = true;
        RecordKeyBtn.Content = "Listening...";
    }

    private void RecordOverlayKeyBtn_Click(object sender, RoutedEventArgs e)
    {
        ResetAllRecordingStates();
        _isRecordingOverlayToggle = true;
        RecordOverlayKeyBtn.Content = "Listening...";
    }

    private void RecordTabletKeyBtn_Click(object sender, RoutedEventArgs e)
    {
        ResetAllRecordingStates();
        _isRecordingTablet = true;
        RecordTabletKeyBtn.Content = "Listening...";
    }

    private void RecordCopilotKeyBtn_Click(object sender, RoutedEventArgs e)
    {
        ResetAllRecordingStates();
        _isRecordingCopilotKey = true;
        RecordCopilotKeyBtn.Content = "Listening...";
    }

    private void RecordCopilotTriggerBtn_Click(object sender, RoutedEventArgs e)
    {
        ResetAllRecordingStates();
        _isRecordingCopilotTrigger = true;
        RecordCopilotTriggerBtn.Content = "Listening...";
    }

    private void ResetAllRecordingStates()
    {
        if (_isRecordingWindowToggle)
        {
            _isRecordingWindowToggle = false;
            RecordKeyBtn.Content = "🔴 Set Key";
        }
        if (_isRecordingOverlayToggle)
        {
            _isRecordingOverlayToggle = false;
            RecordOverlayKeyBtn.Content = "🔴 Set Key";
        }
        if (_isRecordingTablet)
        {
            _isRecordingTablet = false;
            RecordTabletKeyBtn.Content = "🔴 Set Key";
        }
        if (_isRecordingCopilotKey)
        {
            _isRecordingCopilotKey = false;
            RecordCopilotKeyBtn.Content = "🔴 Set Key";
        }
        if (_isRecordingCopilotTrigger)
        {
            _isRecordingCopilotTrigger = false;
            RecordCopilotTriggerBtn.Content = "🔴 Set Key";
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_isRecordingWindowToggle || _isRecordingOverlayToggle || _isRecordingTablet || _isRecordingCopilotKey || _isRecordingCopilotTrigger)
        {
            e.Handled = true;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Check if it's a modifier key itself (only for Window/Overlay/Tablet toggles which require non-modifier key combinations)
            if ((_isRecordingWindowToggle || _isRecordingOverlayToggle || _isRecordingTablet) &&
                (key == Key.LeftCtrl || key == Key.RightCtrl ||
                 key == Key.LeftAlt || key == Key.RightAlt ||
                 key == Key.LeftShift || key == Key.RightShift ||
                 key == Key.LWin || key == Key.RWin))
            {
                if (_isRecordingWindowToggle)
                    UpdateWindowToggleRecordingDisplay();
                else if (_isRecordingOverlayToggle)
                    UpdateOverlayToggleRecordingDisplay();
                else
                    UpdateTabletRecordingDisplay();
                return;
            }

            // If Escape with no modifiers, cancel recording
            if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                ResetAllRecordingStates();
                return;
            }

            // Translate WPF modifiers to Win32 MOD_* flags
            uint modifiers = 0;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) modifiers |= 0x0002;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) modifiers |= 0x0001;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) modifiers |= 0x0004;
            if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) modifiers |= 0x0008;

            var vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk > 0)
            {
                if (_isRecordingWindowToggle)
                {
                    _windowToggleVirtualKey = vk;
                    _windowToggleModifiers = modifiers;
                    UpdateWindowToggleKeyDisplay();
                    _isRecordingWindowToggle = false;
                    RecordKeyBtn.Content = "🔴 Set Key";
                }
                else if (_isRecordingOverlayToggle)
                {
                    _overlayToggleVirtualKey = vk;
                    _overlayToggleModifiers = modifiers;
                    UpdateOverlayToggleKeyDisplay();
                    _isRecordingOverlayToggle = false;
                    RecordOverlayKeyBtn.Content = "🔴 Set Key";
                }
                else if (_isRecordingTablet)
                {
                    _tabletVirtualKey = vk;
                    _tabletModifiers = modifiers;
                    UpdateTabletKeyDisplay();
                    _isRecordingTablet = false;
                    RecordTabletKeyBtn.Content = "🔴 Set Key";
                }
                else if (_isRecordingCopilotKey)
                {
                    _copilotVirtualKey = vk;
                    UpdateCopilotKeyDisplay();
                    _isRecordingCopilotKey = false;
                    RecordCopilotKeyBtn.Content = "🔴 Set Key";
                }
                else if (_isRecordingCopilotTrigger)
                {
                    _copilotTriggerVirtualKey = vk;
                    UpdateCopilotTriggerDisplay();
                    _isRecordingCopilotTrigger = false;
                    RecordCopilotTriggerBtn.Content = "🔴 Set Key";
                }
            }
        }
        else
        {
            base.OnPreviewKeyDown(e);
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        ResetAllRecordingStates();
        base.OnDeactivated(e);
    }

    private void UpdateWindowToggleKeyDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((_windowToggleModifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((_windowToggleModifiers & 0x0001) != 0) parts.Add("Alt");
        if ((_windowToggleModifiers & 0x0004) != 0) parts.Add("Shift");
        if ((_windowToggleModifiers & 0x0008) != 0) parts.Add("Win");

        parts.Add(KeybindViewModel.VkToKeyName(_windowToggleVirtualKey));
        KeyDisplayTextBlock.Text = string.Join(" + ", parts);
    }

    private void UpdateWindowToggleRecordingDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");

        parts.Add("...");
        RecordKeyBtn.Content = string.Join(" + ", parts);
    }

    private void UpdateOverlayToggleKeyDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((_overlayToggleModifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((_overlayToggleModifiers & 0x0001) != 0) parts.Add("Alt");
        if ((_overlayToggleModifiers & 0x0004) != 0) parts.Add("Shift");
        if ((_overlayToggleModifiers & 0x0008) != 0) parts.Add("Win");

        parts.Add(KeybindViewModel.VkToKeyName(_overlayToggleVirtualKey));
        OverlayKeyDisplayTextBlock.Text = string.Join(" + ", parts);
    }

    private void UpdateOverlayToggleRecordingDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");

        parts.Add("...");
        RecordOverlayKeyBtn.Content = string.Join(" + ", parts);
    }

    private void UpdateTabletKeyDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((_tabletModifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((_tabletModifiers & 0x0001) != 0) parts.Add("Alt");
        if ((_tabletModifiers & 0x0004) != 0) parts.Add("Shift");
        if ((_tabletModifiers & 0x0008) != 0) parts.Add("Win");

        parts.Add(KeybindViewModel.VkToKeyName(_tabletVirtualKey));
        TabletKeyDisplayTextBlock.Text = string.Join(" + ", parts);
    }

    private void UpdateTabletRecordingDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");

        parts.Add("...");
        RecordTabletKeyBtn.Content = string.Join(" + ", parts);
    }

    private void UpdateCopilotKeyDisplay()
    {
        if (CopilotKeyDisplayTextBlock != null)
        {
            CopilotKeyDisplayTextBlock.Text = KeybindViewModel.VkToKeyName(_copilotVirtualKey);
        }
    }

    private void UpdateCopilotTriggerDisplay()
    {
        if (CopilotTriggerDisplayTextBlock != null)
        {
            CopilotTriggerDisplayTextBlock.Text = KeybindViewModel.VkToKeyName(_copilotTriggerVirtualKey);
        }
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(XTextBox.Text, out var wtX)) wtX = 100;
        if (!int.TryParse(YTextBox.Text, out var wtY)) wtY = 100;
        if (!int.TryParse(WidthTextBox.Text, out var wtWidth)) wtWidth = 1024;
        if (!int.TryParse(HeightTextBox.Text, out var wtHeight)) wtHeight = 768;

        Settings.WindowToggle = new WindowToggleSettings
        {
            Enabled = EnabledCheckBox.IsChecked == true,
            WindowTitle = TitleComboBox.Text,
            HotkeyVirtualKey = _windowToggleVirtualKey,
            HotkeyModifiers = _windowToggleModifiers,
            X = wtX,
            Y = wtY,
            Width = wtWidth,
            Height = wtHeight
        };

        Settings.OverlayToggleEnabled = OverlayEnabledCheckBox.IsChecked == true;
        Settings.OverlayToggleVirtualKey = _overlayToggleVirtualKey;
        Settings.OverlayToggleModifiers = _overlayToggleModifiers;

        Settings.TabletEnabled = TabletEnabledCheckBox.IsChecked == true;
        Settings.TabletUrl = TabletUrlTextBox.Text;
        Settings.TabletVirtualKey = _tabletVirtualKey;
        Settings.TabletModifiers = _tabletModifiers;

        bool newStart = StartWithWindowsCheckBox.IsChecked == true;
        Settings.StartWithWindows = newStart;
        if (newStart)
        {
            StartupService.EnableStartup();
        }
        else
        {
            StartupService.DisableStartup();
        }

        // Save Copilot settings
        Settings.CopilotEnabled = CopilotEnabledCheckBox.IsChecked == true;
        Settings.CopilotVirtualKey = _copilotVirtualKey;
        Settings.CopilotTriggerVirtualKey = _copilotTriggerVirtualKey;

        DialogResult = true;
        Close();
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private static IntPtr FindWindowByTitleSubstring(string substring)
    {
        IntPtr foundHwnd = IntPtr.Zero;
        EnumWindows((hWnd, lParam) =>
        {
            var sb = new System.Text.StringBuilder(512);
            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (title.Contains(substring, StringComparison.OrdinalIgnoreCase))
            {
                foundHwnd = hWnd;
                return false; // Stop enumeration
            }
            return true; // Continue enumeration
        }, IntPtr.Zero);

        return foundHwnd;
    }

    private void CaptureBtn_Click(object sender, RoutedEventArgs e)
    {
        var targetTitle = TitleComboBox.Text;
        if (string.IsNullOrWhiteSpace(targetTitle))
        {
            MessageBox.Show("Please enter a window title first so I know which window to capture.", "Capture Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IntPtr hWnd = FindWindowByTitleSubstring(targetTitle);
        if (hWnd == IntPtr.Zero)
        {
            MessageBox.Show($"Could not find any open window containing: '{targetTitle}'\n\nPlease make sure the window is open and not minimized or closed.", "Window Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (GetWindowRect(hWnd, out RECT rect))
        {
            int x = rect.Left;
            int y = rect.Top;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            XTextBox.Text = x.ToString();
            YTextBox.Text = y.ToString();
            WidthTextBox.Text = width.ToString();
            HeightTextBox.Text = height.ToString();

            MessageBox.Show($"Successfully captured position!\n\nX: {x}, Y: {y}\nWidth: {width}, Height: {height}", "Capture Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Failed to retrieve the window coordinates.", "Capture Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TitleComboBox_DropDownOpened(object sender, EventArgs e)
    {
        var currentText = TitleComboBox.Text;
        TitleComboBox.Items.Clear();

        EnumWindows((hWnd, lParam) =>
        {
            if (IsWindowVisible(hWnd))
            {
                var sb = new System.Text.StringBuilder(512);
                GetWindowText(hWnd, sb, sb.Capacity);
                var title = sb.ToString();

                if (!string.IsNullOrWhiteSpace(title) && 
                    !title.Contains("MDTab", StringComparison.OrdinalIgnoreCase) &&
                    !title.Contains("MDHelper", StringComparison.OrdinalIgnoreCase) &&
                    !title.Contains("Display Control Settings", StringComparison.OrdinalIgnoreCase))
                {
                    TitleComboBox.Items.Add(title);
                }
            }
            return true;
        }, IntPtr.Zero);

        TitleComboBox.Text = currentText;
    }
}
