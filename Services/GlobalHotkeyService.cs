using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MDHelper.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private readonly Dictionary<int, HotkeyRegistration> _registrations = new();
    private HwndSource? _source;
    private IntPtr _hwnd;
    private bool _hooked;

    public event EventHandler<int>? Triggered; // int = hotkeyId

    public void Initialize(WindowInteropHelper window)
    {
        if (_hooked) return;

        _hwnd = window.Handle;
        _source = HwndSource.FromHwnd(window.Handle);
        if (_source == null)
            throw new InvalidOperationException("Failed to get HwndSource.");

        _source.AddHook(WndProc);
        _hooked = true;
    }

    public void Register(int hotkeyId, uint modifiers, int virtualKey)
    {
        if (!_hooked)
            throw new InvalidOperationException("Initialize must be called before Register.");

        // Unregister existing if any
        if (_registrations.TryGetValue(hotkeyId, out var existing))
        {
            UnregisterHotKey(_hwnd, hotkeyId);
            existing.Modifiers = modifiers;
            existing.VirtualKey = virtualKey;
        }
        else
        {
            existing = new HotkeyRegistration { Id = hotkeyId, Modifiers = modifiers, VirtualKey = virtualKey };
            _registrations[hotkeyId] = existing;
        }

        if (!RegisterHotKey(_hwnd, hotkeyId, modifiers, (uint)virtualKey))
        {
            var err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"RegisterHotKey failed for id={hotkeyId}. Win32Error={err}");
        }

        existing.Registered = true;
    }

    public void Unregister(int hotkeyId)
    {
        if (_registrations.TryGetValue(hotkeyId, out var reg))
        {
            UnregisterHotKey(_hwnd, hotkeyId);
            reg.Registered = false;
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _registrations.Keys)
        {
            UnregisterHotKey(_hwnd, id);
        }
        _registrations.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_registrations.ContainsKey(id))
            {
                handled = true;
                Triggered?.Invoke(this, id);
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        if (_source != null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
        _hooked = false;
    }

    private class HotkeyRegistration
    {
        public int Id { get; set; }
        public uint Modifiers { get; set; }
        public int VirtualKey { get; set; }
        public bool Registered { get; set; }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
