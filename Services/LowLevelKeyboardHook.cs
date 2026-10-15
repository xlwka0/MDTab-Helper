using System;
using System.Runtime.InteropServices;

namespace MDHelper.Services;

public delegate void HookKeyChangedEventHandler(int vkCode, bool isDown, ref bool blockEvent);

public sealed class LowLevelKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    public event HookKeyChangedEventHandler? KeyChanged;

    public LowLevelKeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId == IntPtr.Zero)
        {
            _hookId = SetHook(_proc);
        }
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
            UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    private IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        try
        {
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc, IntPtr.Zero, 0);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            
            // Check if the event was injected by software (LLKHF_INJECTED = 0x10)
            bool isInjected = (kbd.flags & 0x00000010) != 0;

            if (!isInjected)
            {
                bool isDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
                bool isUp = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;

                if (isDown || isUp)
                {
                    bool blockEvent = false;
                    KeyChanged?.Invoke(kbd.vkCode, isDown, ref blockEvent);

                    if (blockEvent)
                    {
                        // Swallow/block the key event from propagating to the OS and active game
                        return (IntPtr)1;
                    }
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    public void Dispose()
    {
        Stop();
    }
}
