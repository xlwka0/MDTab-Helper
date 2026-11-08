using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;

namespace MDHelper.Models;

public readonly record struct ImageStateSnapshot(double X, double Y, double Width, double Height, double Opacity);

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

    [JsonIgnore]
    private readonly Stack<ImageStateSnapshot> _undoHistory = new();

    [JsonIgnore]
    public bool CanUndo => _undoHistory.Count > 0;

    public void PushUndoSnapshot()
    {
        PushUndoSnapshotExplicit(_x, _y, _width, _height, _opacity);
    }

    public void PushUndoSnapshotExplicit(double x, double y, double width, double height, double opacity)
    {
        if (width <= 0 || height <= 0) return;

        // Skip pushing if identical to current top snapshot
        if (_undoHistory.Count > 0)
        {
            var top = _undoHistory.Peek();
            if (Math.Abs(top.X - x) < 0.5 &&
                Math.Abs(top.Y - y) < 0.5 &&
                Math.Abs(top.Width - width) < 0.5 &&
                Math.Abs(top.Height - height) < 0.5 &&
                Math.Abs(top.Opacity - opacity) < 0.01)
            {
                return;
            }
        }

        // Keep maximum 30 snapshots in history
        if (_undoHistory.Count >= 30)
        {
            var list = _undoHistory.ToList();
            list.RemoveAt(list.Count - 1);
            list.Reverse();
            _undoHistory.Clear();
            foreach (var item in list)
            {
                _undoHistory.Push(item);
            }
        }

        _undoHistory.Push(new ImageStateSnapshot(x, y, width, height, opacity));
        OnPropertyChanged(nameof(CanUndo));
    }

    public bool PerformUndo()
    {
        if (_undoHistory.Count == 0) return false;

        var snap = _undoHistory.Pop();
        _x = snap.X;
        _y = snap.Y;
        _width = snap.Width;
        _height = snap.Height;
        _opacity = snap.Opacity;

        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(SizeDisplay));
        OnPropertyChanged(nameof(Opacity));
        OnPropertyChanged(nameof(CanUndo));
        return true;
    }

    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    public string FilePath
    {
        get => _filePath;
        set
        {
            _filePath = value;
            if (string.IsNullOrWhiteSpace(_name))
            {
                _name = System.IO.Path.GetFileName(value);
                OnPropertyChanged(nameof(Name));
            }
            OnPropertyChanged();
        }
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

        if (Path.IsPathRooted(_filePath))
        {
            if (File.Exists(_filePath)) return _filePath;

            string fileName = Path.GetFileName(_filePath);
            string appDataAssetsFallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MDTab-Helper", "Assets", fileName);
            if (File.Exists(appDataAssetsFallback)) return appDataAssetsFallback;

            string appDataRootFallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MDTab-Helper", fileName);
            if (File.Exists(appDataRootFallback)) return appDataRootFallback;

            return _filePath;
        }

        // Relative path: check under %APPDATA%\MDTab-Helper\
        string appDataCombined = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MDTab-Helper", _filePath);
        if (File.Exists(appDataCombined)) return appDataCombined;

        // Relative path: check under %APPDATA%\MDTab-Helper\Assets\
        string fileNameRel = Path.GetFileName(_filePath);
        string appDataAssetsCombined = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MDTab-Helper", "Assets", fileNameRel);
        if (File.Exists(appDataAssetsCombined)) return appDataAssetsCombined;

        // Relative path: check under AppDomain.CurrentDomain.BaseDirectory
        string baseDirCombined = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _filePath);
        if (File.Exists(baseDirCombined)) return baseDirCombined;

        string baseDirAssetsCombined = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileNameRel);
        if (File.Exists(baseDirAssetsCombined)) return baseDirAssetsCombined;

        return appDataCombined;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
