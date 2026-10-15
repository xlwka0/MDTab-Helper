using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace MDHelper.Models;

public class OverlayImage : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N")[..8];
    private string _filePath = string.Empty;
    private string _name = string.Empty;
    private double _x = 100;
    private double _y = 100;
    private double _width = 0;
    private double _height = 0;
    private double _opacity = 1.0;
    private bool _isVisible = true;
    private bool _isLocked = false;
    private bool _maintainAspectRatio = true;
    private int _zIndex = 0;

    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    public string FilePath
    {
        get => _filePath;
        set { _filePath = value; Name = System.IO.Path.GetFileName(value); OnPropertyChanged(); }
    }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public double X
    {
        get => _x;
        set { _x = value; OnPropertyChanged(); }
    }

    public double Y
    {
        get => _y;
        set { _y = value; OnPropertyChanged(); }
    }

    public double Width
    {
        get => _width;
        set { _width = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeDisplay)); }
    }

    public double Height
    {
        get => _height;
        set { _height = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeDisplay)); }
    }

    public string SizeDisplay => $"{Width:F0}x{Height:F0}";

    public double Opacity
    {
        get => _opacity;
        set { _opacity = Math.Clamp(value, 0.1, 1.0); OnPropertyChanged(); }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set { _isVisible = value; OnPropertyChanged(); }
    }

    public bool IsLocked
    {
        get => _isLocked;
        set { _isLocked = value; OnPropertyChanged(); }
    }

    public bool MaintainAspectRatio
    {
        get => _maintainAspectRatio;
        set { _maintainAspectRatio = value; OnPropertyChanged(); }
    }

    public int ZIndex
    {
        get => _zIndex;
        set { _zIndex = value; OnPropertyChanged(); }
    }

    public string GetAbsoluteFilePath()
    {
        if (string.IsNullOrWhiteSpace(_filePath)) return string.Empty;
        if (System.IO.Path.IsPathRooted(_filePath)) return _filePath;
        return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _filePath);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
