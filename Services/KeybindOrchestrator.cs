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
    private const int OVERLAY_TOGGLE_HOTKEY_ID = 9998;
    private const int STOP_TOGGLE_HOTKEY_ID = 9997;
    private const int TABLET_TOGGLE_HOTKEY_ID = 9996;
    private const int KEYBIND_ID_BASE = 1000;
    private readonly GlobalHotkeyService _hotkey = new();
    private readonly ChatSequenceService _sequence;
    private CancellationTokenSource? _runCts;
    private readonly Dictionary<int, KeybindEntry> _entryMap = new(); // hotkeyId -> entry
    private int _nextHotkeyId = KEYBIND_ID_BASE;
    private HwndSource? _hostSource;

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

    public void Initialize(HwndSource source)
    {
        _hostSource = source ?? throw new ArgumentNullException(nameof(source));
        if (_hostSource.Handle == IntPtr.Zero) return;

        _hotkey.Initialize(_hostSource);
        RegisterAll();
    }

    public void Apply(KeybindSettings newSettings)
    {
        if (_hostSource == null || _hostSource.Handle == IntPtr.Zero) return;

        UnregisterAll();
        Settings = newSettings;
        SoundService.Enabled = Settings.SoundsEnabled;
        SettingsChanged?.Invoke(this, EventArgs.Empty);

        RegisterAll();
    }

    private void RegisterAll()
    {
        if (_hostSource == null || _hostSource.Handle == IntPtr.Zero) return;

        _hotkey.Initialize(_hostSource);

        _entryMap.Clear();
        _nextHotkeyId = KEYBIND_ID_BASE;

        if (Settings.GlobalEnabled)
        {
            foreach (var entry in Settings.Entries)
            {
                if (!entry.Enabled) continue;
                if (RoleplayComplianceService.HasBlockedLines(entry.Script, out _, out _)) continue;

                var hotkeyId = _nextHotkeyId++;
                try
                {
                    // Register with MOD_NOREPEAT (0x4000) to prevent auto-repeat when holding down the key
                    _hotkey.Register(hotkeyId, (entry.HotkeyModifiers & 0x000F) | 0x4000, entry.HotkeyVirtualKey);
                    _entryMap[hotkeyId] = entry;
                }
                catch
                {
                    // Skip entries that fail to register
                }
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
        _entryMap.Clear();
    }

    private static bool AreSideModifiersSatisfied(uint mods)
    {
        if ((mods & 0x0010) != 0 && (GetAsyncKeyState(0xA3) & 0x8000) == 0) return false; // RightCtrl
        if ((mods & 0x0020) != 0 && (GetAsyncKeyState(0xA2) & 0x8000) == 0) return false; // LeftCtrl
        if ((mods & 0x0040) != 0 && (GetAsyncKeyState(0xA5) & 0x8000) == 0) return false; // RightAlt
        if ((mods & 0x0080) != 0 && (GetAsyncKeyState(0xA4) & 0x8000) == 0) return false; // LeftAlt
        if ((mods & 0x0100) != 0 && (GetAsyncKeyState(0xA1) & 0x8000) == 0) return false; // RightShift
        if ((mods & 0x0200) != 0 && (GetAsyncKeyState(0xA0) & 0x8000) == 0) return false; // LeftShift
        return true;
    }

    private async void Hotkey_Triggered(object? sender, int hotkeyId)
    {
        if (hotkeyId == OVERLAY_TOGGLE_HOTKEY_ID)
        {
            if (!AreSideModifiersSatisfied(Settings.OverlayToggleModifiers))
                return;

            OverlayToggleRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (hotkeyId == TABLET_TOGGLE_HOTKEY_ID)
        {
            if (!AreSideModifiersSatisfied(Settings.TabletModifiers))
                return;

            TabletToggleRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Emergency STOP hotkey: must ALWAYS trigger to abort running sequences,
        // even if focus temporarily shifted away from the game!
        if (hotkeyId == STOP_TOGGLE_HOTKEY_ID)
        {
            if (!AreSideModifiersSatisfied(Settings.StopHotkeyModifiers))
                return;

            var stopCts = Volatile.Read(ref _runCts);
            try { stopCts?.Cancel(); } catch (ObjectDisposedException) { }
            SoundService.PlayKeybindStop();
            return;
        }

        if (!Settings.GlobalEnabled) return;

        // Ensure custom keybinds only trigger when the game is the active foreground window
        if (!GameProcessHelper.IsGameForeground()) return;

        if (!_entryMap.TryGetValue(hotkeyId, out var entry)) return;
        if (!entry.Enabled) return;
        if (!AreSideModifiersSatisfied(entry.HotkeyModifiers)) return;

        // Diagnostic logging to track exactly what key/process triggered the keybind
        string activeProc = "Unknown";
        try
        {
            IntPtr fgHwnd = GetForegroundWindow();
            GetWindowThreadProcessId(fgHwnd, out uint pid);
            if (pid != 0)
            {
                using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                activeProc = p.ProcessName;
            }
        }
        catch {}

        var held = new List<string>();
        if ((GetAsyncKeyState(0x10) & 0x8000) != 0) held.Add("Shift");
        if ((GetAsyncKeyState(0x11) & 0x8000) != 0) held.Add("Ctrl");
        if ((GetAsyncKeyState(0x12) & 0x8000) != 0) held.Add("Alt");
        if ((GetAsyncKeyState(0x21) & 0x8000) != 0) held.Add("PageUp");
        if ((GetAsyncKeyState(0x22) & 0x8000) != 0) held.Add("PageDown");
        if ((GetAsyncKeyState(0x0D) & 0x8000) != 0) held.Add("Enter");
        if ((GetAsyncKeyState(0x08) & 0x8000) != 0) held.Add("Backspace");
        if ((GetAsyncKeyState(0x20) & 0x8000) != 0) held.Add("Space");

        LogDebug($"Keybind Triggered: ID={hotkeyId}, Name='{entry.Name}', TargetVK={entry.HotkeyVirtualKey}, Foreground='{activeProc}', InGame={GameProcessHelper.IsGameForeground()}, HeldKeys=[{string.Join(", ", held)}]");

        if (RoleplayComplianceService.HasBlockedLines(entry.Script, out _, out _))
        {
            SoundService.PlayKeybindStop();
            return;
        }

        var prevCts = Volatile.Read(ref _runCts);
        try { prevCts?.Cancel(); } catch (ObjectDisposedException) { }

        using var cts = new CancellationTokenSource();
        Volatile.Write(ref _runCts, cts);
        var token = cts.Token;

        // Continually monitor foreground window while keybind executes:
        // If user leaves the game / switches app, kill the keybind immediately!
        var monitorTask = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(25, token).ConfigureAwait(false);
                    if (!GameProcessHelper.IsGameForeground())
                    {
                        cts.Cancel();
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }, token);

        try
        {
            SoundService.PlayKeybindStart();
            await _sequence.RunAsync(Settings, entry, GameProcessHelper.IsGameForeground, token);
            SoundService.PlayKeybindFinish();
        }
        catch (OperationCanceledException)
        {
            // Command was killed because user left the game or pressed stop key
            SoundService.PlayKeybindStop();
        }
        catch (Exception ex)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(ex.Message, "Keybind error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
        finally
        {
            try { cts.Cancel(); } catch { }
            Interlocked.CompareExchange(ref _runCts, null, cts);
            try { await monitorTask.ConfigureAwait(false); } catch { }
        }
    }

    private static void LogDebug(string message)
    {
        try
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MDTab-Helper");
            string file = System.IO.Path.Combine(dir, "keybind_debug.log");

            // Rotate if larger than 1 MB to prevent unbounded file growth
            if (System.IO.File.Exists(file))
            {
                var fi = new System.IO.FileInfo(file);
                if (fi.Length > 1_048_576) // 1 MB
                {
                    string oldFile = System.IO.Path.Combine(dir, "keybind_debug.log.old");
                    System.IO.File.Move(file, oldFile, overwrite: true);
                }
            }

            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            System.IO.File.AppendAllText(file, line);
        }
        catch {}
    }

    public void Dispose()
    {
        var cts = Interlocked.Exchange(ref _runCts, null);
        try
        {
            cts?.Cancel();
        }
        catch
        {
        }

        _hotkey.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static bool IsGta5Foreground()
    {
        return GameProcessHelper.IsGameForeground();
    }
}
