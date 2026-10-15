namespace MDHelper.Models;

public class KeybindEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Keybind";
    public int HotkeyVirtualKey { get; set; } = 0x30; // '0' key as default
    public uint HotkeyModifiers { get; set; } = 0;
    public string Script { get; set; } = "/me exemplu";
    public bool Enabled { get; set; } = true;
}

public class WindowToggleSettings
{
    public bool Enabled { get; set; } = false;
    public string WindowTitle { get; set; } = "MD.B-ZONE.RO";
    public int HotkeyVirtualKey { get; set; } = 0xBD; // default: - (0xBD = VK_OEM_MINUS)
    public uint HotkeyModifiers { get; set; } = 0x0012; // default: RightCtrl (0x0010 | 0x0002)
    public int X { get; set; } = 100;
    public int Y { get; set; } = 100;
    public int Width { get; set; } = 1024;
    public int Height { get; set; } = 768;
}

public class KeybindSettings
{
    public bool GlobalEnabled { get; set; } = false;

    // Chat open key (default: T)
    public int ChatOpenVirtualKey { get; set; } = 0x54; // 'T'

    public int DelayMs { get; set; } = 3000;

    public int DelayAfterChatOpenMs { get; set; } = 250;
    public int DelayAfterTextMs { get; set; } = 250;
    public int DelayAfterEnterMs { get; set; } = 250;

    public List<KeybindEntry> Entries { get; set; } = new()
    {
        new() { Name = "Keybind 1", HotkeyVirtualKey = 0x31, Script = "/me exemplu 1" }, // '1'
        new() { Name = "Keybind 2", HotkeyVirtualKey = 0x32, Script = "/me exemplu 2" }, // '2'
    };

    public WindowToggleSettings WindowToggle { get; set; } = new();

    public bool OverlayToggleEnabled { get; set; } = false;
    public int OverlayToggleVirtualKey { get; set; } = 0xC0; // default: ` (0xC0 = VK_OEM_3)
    public uint OverlayToggleModifiers { get; set; } = 0x0012; // default: RightCtrl (0x0010 | 0x0002)

    public bool StopHotkeyEnabled { get; set; } = true;
    public int StopHotkeyVirtualKey { get; set; } = 0x08; // default: Backspace (0x08)
    public uint StopHotkeyModifiers { get; set; } = 6; // default: Ctrl + Shift (0x0006)

    public bool SoundsEnabled { get; set; } = true;

    // Tablet Overlay Settings
    public bool TabletEnabled { get; set; } = true;
    public string TabletUrl { get; set; } = "https://md.b-zone.ro";
    public int TabletVirtualKey { get; set; } = 0x75; // default: F6 (0x75)
    public uint TabletModifiers { get; set; } = 0; // default: None (0)
    public double TabletOpacity { get; set; } = 0.96;
    public string TabletTheme { get; set; } = "tablet"; // "tablet" or "borderless"
    public bool TabletPlaySounds { get; set; } = true;
    public double TabletZoom { get; set; } = 1.0;
    public int TabletWidth { get; set; } = 1200;
    public int TabletHeight { get; set; } = 820;
    public string TabletWallpaperPath { get; set; } = string.Empty;
    public string TabletLockscreenWallpaperPath { get; set; } = string.Empty;
    public bool StartWithWindows { get; set; } = false;

    // Copilot Settings
    public bool CopilotEnabled { get; set; } = false;
    public int CopilotVirtualKey { get; set; } = 0x57; // default W
    public int CopilotTriggerVirtualKey { get; set; } = 0xBF; // default / (0xBF)
    public uint CopilotTriggerModifiers { get; set; } = 0x0012; // default RightCtrl (0x0010 | 0x0002)
}
