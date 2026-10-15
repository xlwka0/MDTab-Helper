using System;
using System.Threading;
using System.Threading.Tasks;

namespace MDHelper.Services;

public sealed class CopilotService : IDisposable
{
    private readonly LowLevelKeyboardHook _hook = new();
    private volatile bool _isLocked = false;
    private CancellationTokenSource? _holdLoopCts;
    private readonly object _lockObj = new();

    public bool Enabled { get; set; } = false;
    public int CopilotKey { get; set; } = 0x57; // default: W (0x57)
    public int TriggerKey { get; set; } = 0xBF; // default: / (0xBF)
    public uint TriggerModifiers { get; set; } = 0x0012; // default: RightCtrl (0x0010 | 0x0002)

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private bool AreModifiersMatching()
    {
        if (TriggerModifiers == 0) return true;

        // Check RightCtrl specifically if requested
        if ((TriggerModifiers & 0x0010) != 0)
        {
            if ((GetAsyncKeyState(0xA3) & 0x8000) == 0) return false;
        }
        else if ((TriggerModifiers & 0x0020) != 0) // LeftCtrl specifically
        {
            if ((GetAsyncKeyState(0xA2) & 0x8000) == 0) return false;
        }
        else if ((TriggerModifiers & 0x0002) != 0) // Generic Ctrl
        {
            if ((GetAsyncKeyState(0x11) & 0x8000) == 0) return false;
        }

        // Check Alt
        if ((TriggerModifiers & 0x0040) != 0) // RightAlt
        {
            if ((GetAsyncKeyState(0xA5) & 0x8000) == 0) return false;
        }
        else if ((TriggerModifiers & 0x0080) != 0) // LeftAlt
        {
            if ((GetAsyncKeyState(0xA4) & 0x8000) == 0) return false;
        }
        else if ((TriggerModifiers & 0x0001) != 0) // Generic Alt
        {
            if ((GetAsyncKeyState(0x12) & 0x8000) == 0) return false;
        }

        // Check Shift
        if ((TriggerModifiers & 0x0100) != 0) // RightShift
        {
            if ((GetAsyncKeyState(0xA1) & 0x8000) == 0) return false;
        }
        else if ((TriggerModifiers & 0x0200) != 0) // LeftShift
        {
            if ((GetAsyncKeyState(0xA0) & 0x8000) == 0) return false;
        }
        else if ((TriggerModifiers & 0x0004) != 0) // Generic Shift
        {
            if ((GetAsyncKeyState(0x10) & 0x8000) == 0) return false;
        }

        return true;
    }

    public event Action<bool>? StateChanged; // true = locked, false = released

    public bool IsLocked => _isLocked;

    public CopilotService()
    {
        _hook.KeyChanged += Hook_KeyChanged;
    }

    public void Start()
    {
        _hook.Start();
    }

    public void Stop()
    {
        ReleaseLock();
        _hook.Stop();
    }

    private static bool IsMatchingKey(int receivedVk, int targetVk)
    {
        if (receivedVk == targetVk) return true;

        // If target is generic Control (0x11), match either LeftCtrl (0xA2) or RightCtrl (0xA3)
        if (targetVk == 0x11)
        {
            return receivedVk == 0x11 || receivedVk == 0xA2 || receivedVk == 0xA3;
        }

        // If target is generic Shift (0x10), match either LeftShift (0xA0) or RightShift (0xA1)
        if (targetVk == 0x10)
        {
            return receivedVk == 0x10 || receivedVk == 0xA0 || receivedVk == 0xA1;
        }

        // If target is generic Alt / Menu (0x12), match either LeftAlt (0xA4) or RightAlt (0xA5)
        if (targetVk == 0x12)
        {
            return receivedVk == 0x12 || receivedVk == 0xA4 || receivedVk == 0xA5;
        }

        return false;
    }

    private void Hook_KeyChanged(int vkCode, bool isDown, ref bool blockEvent)
    {
        if (!Enabled) return;

        // If not already locked, only engage if GTA5 is the active foreground window
        if (!_isLocked && !KeybindOrchestrator.IsGta5Foreground()) return;

        if (IsMatchingKey(vkCode, CopilotKey))
        {
            if (isDown)
            {
                if (_isLocked)
                {
                    // W pressed again while locked -> disengage autopilot cleanly!
                    ReleaseLock();
                    blockEvent = true; // swallow key down so it doesn't cause a double-tap jerk
                }
            }
            else
            {
                if (_isLocked)
                {
                    // W physically released while locked -> BLOCK physical keyup so Windows/game doesn't stop
                    blockEvent = true;
                }
            }
        }
        else if (IsMatchingKey(vkCode, TriggerKey))
        {
            if (isDown)
            {
                if (AreModifiersMatching())
                {
                    if (!_isLocked)
                    {
                        // Engage autopilot!
                        AcquireLock();
                        blockEvent = true; // swallow trigger key to avoid accidental game actions
                    }
                    else
                    {
                        // Pressing trigger key again toggles autopilot off
                        ReleaseLock();
                        blockEvent = true;
                    }
                }
            }
        }
        else if (_isLocked && isDown)
        {
            // If driving/walking with autopilot and user taps S (brake/reverse), Space (handbrake), or Escape:
            // Automatically disengage autopilot for safety, letting the brake event pass through to the game!
            const int VK_S = 0x53;
            const int VK_SPACE = 0x20;
            const int VK_ESCAPE = 0x1B;

            if (vkCode == VK_S || vkCode == VK_SPACE || vkCode == VK_ESCAPE)
            {
                ReleaseLock();
                // Do not block event so braking or menu takes effect immediately
            }
        }
    }

    private void AcquireLock()
    {
        lock (_lockObj)
        {
            if (_isLocked) return;
            _isLocked = true;

            _holdLoopCts?.Cancel();
            _holdLoopCts?.Dispose();
            _holdLoopCts = new CancellationTokenSource();
            var token = _holdLoopCts.Token;

            var keyToHold = (ushort)CopilotKey;

            // Background holding loop to keep sending scan code key down for game engine compatibility
            Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        SendInputService.ForceKeyDown(keyToHold);
                        await Task.Delay(35, token);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                }
            }, token);

            // Play rising audio tone
            SoundService.PlayOverlayShow();

            StateChanged?.Invoke(true);
        }
    }

    private void ReleaseLock()
    {
        lock (_lockObj)
        {
            if (!_isLocked) return;
            _isLocked = false;

            try
            {
                _holdLoopCts?.Cancel();
                _holdLoopCts?.Dispose();
                _holdLoopCts = null;
            }
            catch
            {
            }

            var keyToRelease = (ushort)CopilotKey;
            SendInputService.ForceKeyUp(keyToRelease);

            // Play falling tone
            SoundService.PlayOverlayHide();

            StateChanged?.Invoke(false);
        }
    }

    public void Dispose()
    {
        Stop();
        _hook.Dispose();
    }
}
