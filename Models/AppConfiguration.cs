namespace MDHelper.Models;

public class AppConfiguration
{
    public List<OverlayImage> Images { get; set; } = new() { CreateDefaultRadioCodesOverlay() };
    public bool AutoStartOverlay { get; set; } = false;
    public string LastSaveLocation { get; set; } = string.Empty;
    public Dictionary<string, object> Settings { get; set; } = new();
    public KeybindSettings Keybind { get; set; } = new();

    public static OverlayImage CreateDefaultRadioCodesOverlay()
    {
        return new OverlayImage
        {
            Name = "Coduri Radio",
            FilePath = "Assets\\coduri_radio.png",
            Width = 380,
            Height = 705,
            X = 30,
            Y = 120,
            Opacity = 1.0,
            IsVisible = true,
            IsLocked = false,
            MaintainAspectRatio = true,
            ZIndex = 0
        };
    }
}
