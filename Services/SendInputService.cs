using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MDHelper.Services;

public sealed class SendInputService
{
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    public async Task KeyPressAsync(ushort virtualKey)
    {
        KeyDown(virtualKey);
        await Task.Delay(35);
        KeyUp(virtualKey);
    }

    public Task KeyDownAsync(ushort virtualKey)
    {
        KeyDown(virtualKey);
        return Task.CompletedTask;
    }

    public Task KeyUpAsync(ushort virtualKey)
    {
        KeyUp(virtualKey);
        return Task.CompletedTask;
    }

    public Task TextAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return Task.CompletedTask;

        var inputs = new INPUT[text.Length * 2];
        int idx = 0;
        foreach (var ch in text)
        {
            inputs[idx++] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = ch,
                        dwFlags = KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            inputs[idx++] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = ch,
                        dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        Send(inputs);
        return Task.CompletedTask;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    public void ReleaseModifiers()
    {
        if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0) KeyUp((ushort)VK_CONTROL);
        if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) KeyUp((ushort)VK_SHIFT);
        if ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0) KeyUp((ushort)VK_MENU);
        if ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0) KeyUp((ushort)VK_LWIN);
        if ((GetAsyncKeyState(VK_RWIN) & 0x8000) != 0) KeyUp((ushort)VK_RWIN);
    }

    private static bool IsExtendedKey(ushort vk)
    {
        return vk == 0x25 || vk == 0x26 || vk == 0x27 || vk == 0x28 || // Arrow keys
               vk == 0x2D || vk == 0x2E || // Insert, Delete
               vk == 0x24 || vk == 0x23 || // Home, End
               vk == 0x21 || vk == 0x22 || // Page Up, Page Down
               vk == 0x90 || // Num Lock
               vk == 0x2C || // Print Screen
               vk == 0x13 || // Pause
               vk == 0x6F || // Numpad /
               vk == 0xA3 || // Right Ctrl
               vk == 0xA5 || // Right Alt
               vk == 0x5B || // Left Win
               vk == 0x5C || // Right Win
               vk == 0x5D;   // Apps/Menu
    }

    private static INPUT CreateKeyInput(ushort vk, bool isKeyUp)
    {
        const uint MAPVK_VK_TO_VSC_EX = 4;
        uint scanEx = MapVirtualKey(vk, MAPVK_VK_TO_VSC_EX);
        if (scanEx == 0)
        {
            scanEx = MapVirtualKey(vk, 0);
        }

        var inp = new INPUT
        {
            type = INPUT_KEYBOARD
        };
        inp.U.ki.wVk = vk;
        inp.U.ki.time = 0;
        inp.U.ki.dwExtraInfo = IntPtr.Zero;

        if (scanEx != 0)
        {
            inp.U.ki.wScan = (ushort)(scanEx & 0xFF);
            inp.U.ki.dwFlags = KEYEVENTF_SCANCODE | (isKeyUp ? KEYEVENTF_KEYUP : 0u);
            if (((scanEx >> 8) & 0xFF) == 0xE0 || IsExtendedKey(vk))
            {
                inp.U.ki.dwFlags |= KEYEVENTF_EXTENDEDKEY;
            }
        }
        else
        {
            // Fallback to virtual-key injection when the current layout has no scan code
            inp.U.ki.wScan = 0;
            inp.U.ki.dwFlags = isKeyUp ? KEYEVENTF_KEYUP : 0u;
            if (IsExtendedKey(vk))
            {
                inp.U.ki.dwFlags |= KEYEVENTF_EXTENDEDKEY;
            }
        }

        return inp;
    }

    private static void KeyDown(ushort vk)
    {
        Send(new[] { CreateKeyInput(vk, isKeyUp: false) });
    }

    private static void KeyUp(ushort vk)
    {
        Send(new[] { CreateKeyInput(vk, isKeyUp: true) });
    }

    public static void ForceKeyDown(ushort vk)
    {
        KeyDown(vk);
    }

    public static void ForceKeyUp(ushort vk)
    {
        KeyUp(vk);
    }

    private static void Send(INPUT[] inputs)
    {
        var size = Marshal.SizeOf(typeof(INPUT));
        var sent = SendInput((uint)inputs.Length, inputs, size);
        if (sent != inputs.Length)
        {
            var err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SendInput failed. Sent={sent}/{inputs.Length}. Win32Error={err}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
