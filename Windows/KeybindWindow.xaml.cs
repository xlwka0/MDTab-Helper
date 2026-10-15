using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MDHelper.Models;

namespace MDHelper.Windows;

public partial class KeybindWindow : Window
{
    private ObservableCollection<KeybindViewModel> _entries = new();
    private KeybindViewModel? _recordingEntry;
    private bool _isRecording = false;

    private int _stopHotkeyVirtualKey = 0x08; // default Backspace
    private uint _stopHotkeyModifiers = 6; // default Ctrl + Shift
    private bool _isRecordingStopKey = false;

    public KeybindSettings Settings { get; private set; }

    public KeybindWindow(KeybindSettings settings)
    {
        InitializeComponent();
        Settings = settings;

        // Load global settings
        GlobalEnabledCheckBox.IsChecked = settings.GlobalEnabled;
        DelayTextBox.Text = settings.DelayMs.ToString();
        DelayAfterChatOpenTextBox.Text = settings.DelayAfterChatOpenMs.ToString();
        DelayAfterTextTextBox.Text = settings.DelayAfterTextMs.ToString();
        DelayAfterEnterTextBox.Text = settings.DelayAfterEnterMs.ToString();
        SoundsEnabledCheckBox.IsChecked = settings.SoundsEnabled;

        // Load stop hotkey settings
        StopEnabledCheckBox.IsChecked = settings.StopHotkeyEnabled;
        _stopHotkeyVirtualKey = settings.StopHotkeyVirtualKey;
        _stopHotkeyModifiers = settings.StopHotkeyModifiers;
        UpdateStopKeyDisplay();

        // Load entries
        foreach (var entry in settings.Entries)
        {
            _entries.Add(new KeybindViewModel(entry));
        }

        KeybindDataGrid.ItemsSource = _entries;
    }

    private void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        var entry = new KeybindEntry
        {
            Name = $"Keybind {_entries.Count + 1}",
            HotkeyVirtualKey = 0x30, // '0'
            Script = "/me text"
        };
        _entries.Add(new KeybindViewModel(entry));
    }

    private void EditBtn_Click(object sender, RoutedEventArgs e)
    {
        if (KeybindDataGrid.SelectedItem is KeybindViewModel vm)
        {
            var dialog = new KeybindEditDialog(vm.Entry);
            if (dialog.ShowDialog() == true)
            {
                vm.Refresh();
            }
        }
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (KeybindDataGrid.SelectedItem is KeybindViewModel vm)
        {
            _entries.Remove(vm);
        }
    }

    private void RecordKeyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (KeybindDataGrid.SelectedItem is not KeybindViewModel vm)
        {
            MessageBox.Show("Select a keybind first.", "Set Key", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _recordingEntry = vm;
        _isRecording = true;
        RecordKeyBtn.Content = "Listening...";
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_isRecording || _isRecordingStopKey)
        {
            e.Handled = true;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Check if it's a modifier key itself
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                if (_isRecording)
                    UpdateRecordingDisplay();
                else
                    UpdateStopKeyRecordingDisplay();
                return;
            }

            // If Escape with no modifiers, cancel recording
            if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (_isRecording)
                {
                    _isRecording = false;
                    _recordingEntry = null;
                    RecordKeyBtn.Content = "🔴 Set Key";
                }
                else
                {
                    _isRecordingStopKey = false;
                    RecordStopKeyBtn.Content = "🔴 Set Key";
                }
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
                if (_isRecording && _recordingEntry != null)
                {
                    _recordingEntry.Entry.HotkeyVirtualKey = vk;
                    _recordingEntry.Entry.HotkeyModifiers = modifiers;
                    _recordingEntry.Refresh();
                    
                    _isRecording = false;
                    _recordingEntry = null;
                    RecordKeyBtn.Content = "🔴 Set Key";
                }
                else if (_isRecordingStopKey)
                {
                    _stopHotkeyVirtualKey = vk;
                    _stopHotkeyModifiers = modifiers;
                    UpdateStopKeyDisplay();
                    
                    _isRecordingStopKey = false;
                    RecordStopKeyBtn.Content = "🔴 Set Key";
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
        if (_isRecording)
        {
            _isRecording = false;
            _recordingEntry = null;
            RecordKeyBtn.Content = "🔴 Set Key";
        }
        if (_isRecordingStopKey)
        {
            _isRecordingStopKey = false;
            RecordStopKeyBtn.Content = "🔴 Set Key";
        }
        base.OnDeactivated(e);
    }

    private void UpdateRecordingDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        
        parts.Add("...");
        RecordKeyBtn.Content = string.Join(" + ", parts);
    }

    private void RecordStopKeyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            _isRecording = false;
            _recordingEntry = null;
            RecordKeyBtn.Content = "🔴 Set Key";
        }
        _isRecordingStopKey = true;
        RecordStopKeyBtn.Content = "Listening...";
    }

    private void UpdateStopKeyDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((_stopHotkeyModifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((_stopHotkeyModifiers & 0x0001) != 0) parts.Add("Alt");
        if ((_stopHotkeyModifiers & 0x0004) != 0) parts.Add("Shift");
        if ((_stopHotkeyModifiers & 0x0008) != 0) parts.Add("Win");

        parts.Add(KeybindViewModel.VkToKeyName(_stopHotkeyVirtualKey));
        StopKeyDisplayTextBlock.Text = string.Join(" + ", parts);
    }

    private void UpdateStopKeyRecordingDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");

        parts.Add("...");
        RecordStopKeyBtn.Content = string.Join(" + ", parts);
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(DelayTextBox.Text, out var delayMs))
            delayMs = 3000;
        delayMs = Math.Clamp(delayMs, 0, 60000);

        if (!int.TryParse(DelayAfterChatOpenTextBox.Text, out var delayAfterChatOpenMs))
            delayAfterChatOpenMs = 250;
        delayAfterChatOpenMs = Math.Clamp(delayAfterChatOpenMs, 0, 10000);

        if (!int.TryParse(DelayAfterTextTextBox.Text, out var delayAfterTextMs))
            delayAfterTextMs = 250;
        delayAfterTextMs = Math.Clamp(delayAfterTextMs, 0, 10000);

        if (!int.TryParse(DelayAfterEnterTextBox.Text, out var delayAfterEnterMs))
            delayAfterEnterMs = 250;
        delayAfterEnterMs = Math.Clamp(delayAfterEnterMs, 0, 10000);

        Settings = new KeybindSettings
        {
            GlobalEnabled = GlobalEnabledCheckBox.IsChecked == true,
            ChatOpenVirtualKey = Settings.ChatOpenVirtualKey,
            DelayMs = delayMs,
            DelayAfterChatOpenMs = delayAfterChatOpenMs,
            DelayAfterTextMs = delayAfterTextMs,
            DelayAfterEnterMs = delayAfterEnterMs,
            Entries = _entries.Select(vm => vm.Entry).ToList(),
            WindowToggle = Settings.WindowToggle, // Preserve window control settings!

            OverlayToggleEnabled = Settings.OverlayToggleEnabled,
            OverlayToggleVirtualKey = Settings.OverlayToggleVirtualKey,
            OverlayToggleModifiers = Settings.OverlayToggleModifiers,

            StopHotkeyEnabled = StopEnabledCheckBox.IsChecked == true,
            StopHotkeyVirtualKey = _stopHotkeyVirtualKey,
            StopHotkeyModifiers = _stopHotkeyModifiers,

            SoundsEnabled = SoundsEnabledCheckBox.IsChecked == true,

            TabletEnabled = Settings.TabletEnabled,
            TabletUrl = Settings.TabletUrl,
            TabletVirtualKey = Settings.TabletVirtualKey,
            TabletModifiers = Settings.TabletModifiers,
            TabletOpacity = Settings.TabletOpacity,
            TabletTheme = Settings.TabletTheme,
            TabletPlaySounds = Settings.TabletPlaySounds,
            TabletZoom = Settings.TabletZoom,
            TabletWidth = Settings.TabletWidth,
            TabletHeight = Settings.TabletHeight,
            TabletWallpaperPath = Settings.TabletWallpaperPath,
            TabletLockscreenWallpaperPath = Settings.TabletLockscreenWallpaperPath,
            StartWithWindows = Settings.StartWithWindows,

            CopilotEnabled = Settings.CopilotEnabled,
            CopilotVirtualKey = Settings.CopilotVirtualKey,
            CopilotTriggerVirtualKey = Settings.CopilotTriggerVirtualKey,
            CopilotTriggerModifiers = Settings.CopilotTriggerModifiers
        };

        DialogResult = true;
        Close();
    }
}

public class KeybindViewModel : INotifyPropertyChanged
{
    public KeybindEntry Entry { get; }

    public string Name
    {
        get => Entry.Name;
        set { Entry.Name = value; OnPropertyChanged(); }
    }

    public bool Enabled
    {
        get => Entry.Enabled;
        set { Entry.Enabled = value; OnPropertyChanged(); }
    }

    public string KeyDisplay
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if ((Entry.HotkeyModifiers & 0x0002) != 0) parts.Add("Ctrl");
            if ((Entry.HotkeyModifiers & 0x0001) != 0) parts.Add("Alt");
            if ((Entry.HotkeyModifiers & 0x0004) != 0) parts.Add("Shift");
            if ((Entry.HotkeyModifiers & 0x0008) != 0) parts.Add("Win");
            
            parts.Add(VkToKeyName(Entry.HotkeyVirtualKey));
            return string.Join(" + ", parts);
        }
    }

    public string ScriptPreview
    {
        get
        {
            var firstLine = Entry.Script?.Split('\n').FirstOrDefault() ?? "";
            if (firstLine.Length > 40) firstLine = firstLine[..40] + "...";
            return firstLine;
        }
    }

    public KeybindViewModel(KeybindEntry entry)
    {
        Entry = entry;
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(KeyDisplay));
        OnPropertyChanged(nameof(ScriptPreview));
    }

    public static string VkToKeyName(int vk)
    {
        // Try to get key name from virtual key code
        var key = KeyInterop.KeyFromVirtualKey(vk);
        var name = key.ToString();
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]))
            return name[1].ToString();
        if (name.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) && name.Length == 7 && char.IsDigit(name[6]))
            return $"Num {name[6]}";
        return name switch
        {
            "Return" => "Enter",
            "Back" => "Backspace",
            "Capital" => "CapsLock",
            "Escape" => "Esc",
            "OemQuestion" => "/",
            "OemPeriod" => ".",
            "OemComma" => ",",
            "OemMinus" => "-",
            "OemPlus" => "+",
            "OemTilde" => "`",
            "Oem3" => "`",
            "OemOpenBrackets" => "[",
            "OemCloseBrackets" => "]",
            "OemPipe" => "\\",
            "OemQuotes" => "'",
            "OemSemicolon" => ";",
            "Divide" => "Num /",
            "Multiply" => "Num *",
            "Subtract" => "Num -",
            "Add" => "Num +",
            "Decimal" => "Num .",
            _ => name
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class KeybindEditDialog : Window
{
    public KeybindEntry Entry { get; }
    private TextBox _nameBox = null!;
    private TextBox _scriptBox = null!;

    public KeybindEditDialog(KeybindEntry entry)
    {
        Entry = entry;
        Title = "Edit Keybind";
        Width = 500;
        Height = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 15, 26));

        BuildUI();
    }

    private void BuildUI()
    {
        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Edit Keybind",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 14)
        };
        grid.Children.Add(title);
        Grid.SetRow(title, 0);

        var namePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        namePanel.Children.Add(new TextBlock { Text = "Name:", Width = 80, Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        _nameBox = new TextBox { Text = Entry.Name, Width = 300, Height = 28, Foreground = System.Windows.Media.Brushes.White, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 46)), BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51)) };
        namePanel.Children.Add(_nameBox);
        grid.Children.Add(namePanel);
        Grid.SetRow(namePanel, 1);

        var scriptPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
        scriptPanel.Children.Add(new TextBlock { Text = "Script (one line = one message):", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 5) });
        _scriptBox = new TextBox
        {
            Text = Entry.Script,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.Wrap,
            Height = 150,
            Foreground = System.Windows.Media.Brushes.White,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 46)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51))
        };
        scriptPanel.Children.Add(_scriptBox);
        grid.Children.Add(scriptPanel);
        Grid.SetRow(scriptPanel, 2);

        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelBtn = new Button { Content = "Cancel", Width = 100, Height = 32, Margin = new Thickness(0, 0, 10, 0), Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51)), Foreground = System.Windows.Media.Brushes.White };
        cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
        var saveBtn = new Button { Content = "OK", Width = 100, Height = 32, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 122, 204)), Foreground = System.Windows.Media.Brushes.White };
        saveBtn.Click += OnSave;
        buttonPanel.Children.Add(cancelBtn);
        buttonPanel.Children.Add(saveBtn);
        grid.Children.Add(buttonPanel);
        Grid.SetRow(buttonPanel, 3);

        Content = grid;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        Entry.Name = _nameBox.Text;
        Entry.Script = _scriptBox.Text;
        DialogResult = true;
        Close();
    }
}
