using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace MDHelper.Services;

public sealed class TrayIconService : IDisposable
{
    private const int WM_USER = 0x0400;
    public const int WM_TRAYICON = WM_USER + 1024;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private IntPtr _hwnd;
    private HwndSource? _source;
    private bool _added;
    private IntPtr _hIcon = IntPtr.Zero;

    public Action? LeftClick { get; set; }
    public ContextMenu? ContextMenu { get; set; }

    public void Initialize(Window window, string tooltip, string? iconPath = null)
    {
        var helper = new WindowInteropHelper(window);
        _hwnd = helper.EnsureHandle();

        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        // Load Icon
        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
        {
            _hIcon = LoadImage(IntPtr.Zero, iconPath, 1 /*IMAGE_ICON*/, 16, 16, 0x00000010 /*LR_LOADFROMFILE*/);
        }

        if (_hIcon == IntPtr.Zero)
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                ExtractIconEx(exePath, 0, out _, out _hIcon, 1);
            }
        }
        
        if (_hIcon == IntPtr.Zero)
        {
            _hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512 /*IDI_APPLICATION*/);
        }

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = tooltip.Length > 63 ? tooltip[..63] : tooltip
        };

        _added = Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            int eventId = lParam.ToInt32();
            if (eventId == WM_LBUTTONUP || eventId == WM_LBUTTONDBLCLK)
            {
                handled = true;
                LeftClick?.Invoke();
            }
            else if (eventId == WM_RBUTTONUP)
            {
                handled = true;
                ShowContextMenu();
            }
        }

        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        if (ContextMenu == null) return;

        SetForegroundWindow(_hwnd);
        ContextMenu.Placement = PlacementMode.MousePoint;
        ContextMenu.IsOpen = true;
    }

    public void Dispose()
    {
        if (_added && _hwnd != IntPtr.Zero)
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _added = false;
        }

        if (_source != null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpdata);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string szFileName, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
