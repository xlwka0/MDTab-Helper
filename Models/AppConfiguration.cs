namespace MDHelper.Models;

public class AppConfiguration
{
    public List<OverlayImage> Images { get; set; } = new();
    public bool AutoStartOverlay { get; set; } = false;
    public string LastSaveLocation { get; set; } = string.Empty;
    public Dictionary<string, object> Settings { get; set; } = new();
    public KeybindSettings Keybind { get; set; } = new();
}
