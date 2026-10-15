using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MDHelper.Models;

namespace MDHelper.Services;

public sealed class KeybindOrchestrator : IDisposable
{
    private const int WINDOW_TOGGLE_HOTKEY_ID = 9999;
    private const int OVERLAY_TOGGLE_HOTKEY_ID = 9998;
    private const int STOP_TOGGLE_HOTKEY_ID = 9997;
    private const int TABLET_TOGGLE_HOTKEY_ID = 9996;
    private readonly GlobalHotkeyService _hotkey = new();
    private readonly ChatSequenceService _sequence;
    private CancellationTokenSource? _runCts;
    private readonly Dictionary<int, KeybindEntry> _entryMap = new(); // hotkeyId -> entry
    private int _nextHotkeyId = 1;
    private IntPtr _previousActiveWindow = IntPtr.Zero;

    public KeybindSettings Settings { get; private set; }

    public event EventHandler? SettingsChanged;
    public event EventHandler? OverlayToggleRequested;
    public event EventHandler? TabletToggleRequested;

    public KeybindOrchestrator(KeybindSettings settings)
    {
        Settings = settings;
        SoundService.Enabled = Settings.SoundsEnabled;
        _sequence = new ChatSequenceService(new SendInputService());
        _hotkey.Triggered += Hotkey_Triggered;
    }

    public void Initialize(Window window)
    {
        var helper = new WindowInteropHelper(window);
        if (helper.Handle == IntPtr.Zero) return;
        
        _hotkey.Initialize(helper);
        RegisterAll(window);
    }

    public void Apply(Window window, KeybindSettings newSettings)
    {
        UnregisterAll();
        Settings = newSettings;
        SoundService.Enabled = Settings.SoundsEnabled;
        SettingsChanged?.Invoke(this, EventArgs.Empty);

        RegisterAll(window);
    }

    private void RegisterAll(Window window)
    {
        var helper = new WindowInteropHelper(window);
        if (helper.Handle == IntPtr.Zero) return;
        
        _hotkey.Initialize(helper);

        _entryMap.Clear();
        _nextHotkeyId = 1;

        if (Settings.GlobalEnabled)
        {
            foreach (var entry in Settings.Entries)
            {
                if (!entry.Enabled) continue;

                var hotkeyId = _nextHotkeyId++;
                try
                {
                    // Register with MOD_NOREPEAT (0x4000) to prevent auto-repeat when holding down the key
                    _hotkey.Register(hotkeyId, entry.HotkeyModifiers | 0x4000, entry.HotkeyVirtualKey);
                    _entryMap[hotkeyId] = entry;
                }
                catch
                {
                    // Skip entries that fail to register
                }
            }
        }

        if (Settings.WindowToggle != null && Settings.WindowToggle.Enabled)
        {
            try
            {
                _hotkey.Register(WINDOW_TOGGLE_HOTKEY_ID, (Settings.WindowToggle.HotkeyModifiers & 0x000F) | 0x4000, Settings.WindowToggle.HotkeyVirtualKey);
            }
            catch
            {
                // Skip if registration fails
            }
        }

        if (Settings.OverlayToggleEnabled)
        {
            try
            {
                _hotkey.Register(OVERLAY_TOGGLE_HOTKEY_ID, (Settings.OverlayToggleModifiers & 0x000F) | 0x4000, Settings.OverlayToggleVirtualKey);
            }
            catch
            {
                // Skip if registration fails
            }
        }

        if (Settings.StopHotkeyEnabled)
        {
            try
            {
                _hotkey.Register(STOP_TOGGLE_HOTKEY_ID, (Settings.StopHotkeyModifiers & 0x000F) | 0x4000, Settings.StopHotkeyVirtualKey);
            }
            catch
            {
                // Skip if registration fails
            }
        }

        if (Settings.TabletEnabled)
        {
            try
            {
                _hotkey.Register(TABLET_TOGGLE_HOTKEY_ID, (Settings.TabletModifiers & 0x000F) | 0x4000, Settings.TabletVirtualKey);
            }
            catch
            {
                // Skip if registration fails
            }
        }
    }

    private void UnregisterAll()
    {
        _hotkey.UnregisterAll();
        _hotkey.Unregister(WINDOW_TOGGLE_HOTKEY_ID);
        _hotkey.Unregister(OVERLAY_TOGGLE_HOTKEY_ID);
        _hotkey.Unregister(STOP_TOGGLE_HOTKEY_ID);
        _hotkey.Unregister(TABLET_TOGGLE_HOTKEY_ID);
        _entryMap.Clear();
    }

    private async void Hotkey_Triggered(object? sender, int hotkeyId)
    {
        if (hotkeyId == WINDOW_TOGGLE_HOTKEY_ID)
        {
            if (Settings.WindowToggle != null)
            {
                if ((Settings.WindowToggle.HotkeyModifiers & 0x0010) != 0)
                {
                    if ((GetAsyncKeyState(0xA3) & 0x8000) == 0) return; // RightCtrl required
                }
                else if ((Settings.WindowToggle.HotkeyModifiers & 0x0020) != 0)
                {
                    if ((GetAsyncKeyState(0xA2) & 0x8000) == 0) return; // LeftCtrl required
                }
            }

            ToggleExternalWindow();
            return;
        }

        if (hotkeyId == OVERLAY_TOGGLE_HOTKEY_ID)
        {
            if ((Settings.OverlayToggleModifiers & 0x0010) != 0)
            {
                if ((GetAsyncKeyState(0xA3) & 0x8000) == 0) return; // RightCtrl required
            }
            else if ((Settings.OverlayToggleModifiers & 0x0020) != 0)
            {
                if ((GetAsyncKeyState(0xA2) & 0x8000) == 0) return; // LeftCtrl required
            }

            OverlayToggleRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (hotkeyId == TABLET_TOGGLE_HOTKEY_ID)
        {
            TabletToggleRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!Settings.GlobalEnabled) return;

        // Ensure keybinds only trigger when GTA5 is the active foreground window
        if (!IsGta5Foreground()) return;

        if (hotkeyId == STOP_TOGGLE_HOTKEY_ID)
        {
            _runCts?.Cancel();
            SoundService.PlayKeybindStop();
            return;
        }

        if (!_entryMap.TryGetValue(hotkeyId, out var entry)) return;

        try
        {
            _runCts?.Cancel();
            _runCts = new CancellationTokenSource();
            SoundService.PlayKeybindStart();
            await _sequence.RunAsync(Settings, entry, _runCts.Token);
            SoundService.PlayKeybindFinish();
        }
        catch (OperationCanceledException)
        {
            // Ignore when canceled
        }
        catch (Exception ex)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(ex.Message, "Keybind error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
    }

    public void Dispose()
    {
        try
        {
            _runCts?.Cancel();
        }
        catch
        {
        }

        _hotkey.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static bool IsGta5Foreground()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return false;

        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            string name = proc.ProcessName;
            return name.Equals("gta5", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("GTA5", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("gta5", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("GTA5", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("PlayGTAV", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_SHOWWINDOW = 0x0040;

    private void ToggleExternalWindow()
    {
        if (Settings.WindowToggle == null) return;
        var targetTitle = Settings.WindowToggle.WindowTitle;
        if (string.IsNullOrWhiteSpace(targetTitle)) return;

        IntPtr hWnd = FindWindowByTitleSubstring(targetTitle);
        if (hWnd == IntPtr.Zero)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"Could not find window containing: '{targetTitle}'", "Window Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
            return;
        }

        if (IsWindowVisible(hWnd))
        {
            ShowWindow(hWnd, SW_HIDE);
            SoundService.PlayOverlayHide();
            
            // Restore focus to previous foreground window
            if (_previousActiveWindow != IntPtr.Zero && IsWindow(_previousActiveWindow))
            {
                SetForegroundWindow(_previousActiveWindow);
                _previousActiveWindow = IntPtr.Zero;
            }
        }
        else
        {
            // Save current foreground window before showing Chrome
            IntPtr currentForeground = GetForegroundWindow();
            if (currentForeground != IntPtr.Zero && currentForeground != hWnd)
            {
                _previousActiveWindow = currentForeground;
            }

            // Show window
            ShowWindow(hWnd, SW_SHOW);
            SoundService.PlayOverlayShow();
            
            // Resize and move to configured position, make it Topmost
            SetWindowPos(
                hWnd, 
                HWND_TOPMOST, 
                Settings.WindowToggle.X, 
                Settings.WindowToggle.Y, 
                Settings.WindowToggle.Width, 
                Settings.WindowToggle.Height, 
                SWP_SHOWWINDOW
            );
            
            // Bring to foreground
            SetForegroundWindow(hWnd);
        }
    }

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
}
