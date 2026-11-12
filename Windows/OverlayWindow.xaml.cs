using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MDHelper.Models;
using MDHelper.Services;

namespace MDHelper.Windows;

public partial class OverlayWindow : Window
{
    private OverlayImage _imageData = null!;
    private readonly ConfigurationService _configService;
    private bool _isUpdatingFromWindow = false;
    private bool _isResizing = false;
    private Point _resizeStart;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private double _aspectRatio = 1;
    private DateTime _lastWheelSnapshotTime = DateTime.MinValue;

    public OverlayImage ImageData => _imageData;

    public event EventHandler<OverlayImage>? ImageRemoved;
    public event EventHandler<OverlayImage>? PositionChanged;

    public OverlayWindow(OverlayImage imageData, ConfigurationService configService)
    {
        InitializeComponent();
        _configService = configService;
        _imageData = imageData;
        
        LoadImage();
        SetupBindings();
        UpdateWindowPosition();
    }

    private void LoadImage()
    {
        try
        {
            string absPath = _imageData.GetAbsoluteFilePath();
            if (!File.Exists(absPath))
            {
                MessageBox.Show($"Image not found: {absPath}\n\nPlease add a valid image first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                // Don't close - let user add image from main window
                return;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(absPath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();

            OverlayImage.Source = bitmap;
            _aspectRatio = bitmap.PixelHeight > 0 ? bitmap.PixelWidth / (double)bitmap.PixelHeight : 1.0;

            // Set initial size if not already set
            if (_imageData.Width <= 0 || _imageData.Height <= 0)
            {
                _imageData.Width = bitmap.PixelWidth;
                _imageData.Height = bitmap.PixelHeight;
            }
            else if (_imageData.MaintainAspectRatio)
            {
                _imageData.Height = _imageData.Width / _aspectRatio;
            }

            double screenW = SystemParameters.PrimaryScreenWidth > 0 ? SystemParameters.PrimaryScreenWidth : 1920;
            double screenH = SystemParameters.PrimaryScreenHeight > 0 ? SystemParameters.PrimaryScreenHeight : 1080;
            _imageData.X = Math.Clamp(_imageData.X, 0, Math.Max(0, screenW - _imageData.Width));
            _imageData.Y = Math.Clamp(_imageData.Y, 0, Math.Max(0, screenH - _imageData.Height));

            UpdateWindowPosition();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetupBindings()
    {
        UpdateOpacity();
        _imageData.PropertyChanged += ImageData_PropertyChanged;
    }

    private void ImageData_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUpdatingFromWindow) return;

        switch (e.PropertyName)
        {
            case nameof(Models.OverlayImage.Opacity):
                UpdateOpacity();
                break;
            case nameof(Models.OverlayImage.IsLocked):
                UpdateClickThrough();
                break;
            case nameof(Models.OverlayImage.X):
            case nameof(Models.OverlayImage.Y):
            case nameof(Models.OverlayImage.Width):
            case nameof(Models.OverlayImage.Height):
                UpdateWindowPosition();
                break;
        }
    }

    private void UpdateOpacity()
    {
        Opacity = _imageData.Opacity;
    }

    private void UpdateWindowPosition()
    {
        Left = _imageData.X;
        Top = _imageData.Y;
        Width = _imageData.Width;
        Height = _imageData.Height;
    }

    private void UpdateWindowSize()
    {
        Width = _imageData.Width;
        Height = _imageData.Height;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_imageData.IsLocked) return;

        double preDragX = _imageData.X;
        double preDragY = _imageData.Y;
        double preDragW = _imageData.Width;
        double preDragH = _imageData.Height;
        double preDragOp = _imageData.Opacity;
        
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Ignore if mouse button state changed
        }

        double screenW = SystemParameters.PrimaryScreenWidth > 0 ? SystemParameters.PrimaryScreenWidth : 1920;
        double screenH = SystemParameters.PrimaryScreenHeight > 0 ? SystemParameters.PrimaryScreenHeight : 1080;

        _isUpdatingFromWindow = true;
        try
        {
            Left = Math.Clamp(Left, 0, Math.Max(0, screenW - Width));
            Top = Math.Clamp(Top, 0, Math.Max(0, screenH - Height));

            _imageData.X = Left;
            _imageData.Y = Top;
            SavePosition();

            if (Math.Abs(_imageData.X - preDragX) > 1 || Math.Abs(_imageData.Y - preDragY) > 1)
            {
                _imageData.PushUndoSnapshotExplicit(preDragX, preDragY, preDragW, preDragH, preDragOp);
            }
        }
        finally
        {
            _isUpdatingFromWindow = false;
        }
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        // DragMove handles everything
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_imageData == null || _imageData.IsLocked || Width <= 0 || Height <= 0) return;

        if ((DateTime.UtcNow - _lastWheelSnapshotTime).TotalMilliseconds > 800)
        {
            _imageData.PushUndoSnapshot();
        }
        _lastWheelSnapshotTime = DateTime.UtcNow;

        var mousePos = e.GetPosition(this);
        double mouseX = mousePos.X;
        double mouseY = mousePos.Y;

        double screenW = SystemParameters.PrimaryScreenWidth > 0 ? SystemParameters.PrimaryScreenWidth : 1920;
        double screenH = SystemParameters.PrimaryScreenHeight > 0 ? SystemParameters.PrimaryScreenHeight : 1080;

        var delta = e.Delta > 0 ? 1.05 : 0.95;
        var newWidth = Math.Clamp(Width * delta, 40, screenW);
        var newHeight = _imageData.MaintainAspectRatio 
            ? Math.Clamp(newWidth / _aspectRatio, 40, screenH)
            : Math.Clamp(Height * delta, 40, screenH);

        if (Math.Abs(newWidth - Width) < 0.5 && Math.Abs(newHeight - Height) < 0.5) return;

        // Anchor calculations to scale centered on the mouse cursor
        double x_rel = mouseX / Width;
        double y_rel = mouseY / Height;

        _isUpdatingFromWindow = true;
        try
        {
            double newLeft = Left - (newWidth - Width) * x_rel;
            double newTop = Top - (newHeight - Height) * y_rel;

            Left = Math.Clamp(newLeft, 0, Math.Max(0, screenW - newWidth));
            Top = Math.Clamp(newTop, 0, Math.Max(0, screenH - newHeight));
            Width = newWidth;
            Height = newHeight;

            _imageData.X = Left;
            _imageData.Y = Top;
            _imageData.Width = newWidth;
            _imageData.Height = newHeight;
            SavePosition();
        }
        finally
        {
            _isUpdatingFromWindow = false;
        }
    }

    private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_imageData.IsLocked) return;
        
        _isResizing = true;
        _resizeStart = e.GetPosition(this);
        _resizeStartWidth = Width;
        _resizeStartHeight = Height;
        (sender as UIElement)?.CaptureMouse();
        e.Handled = true;
    }

    private void ResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isResizing) return;
        _isResizing = false;
        (sender as UIElement)?.ReleaseMouseCapture();
        if (Math.Abs(Width - _resizeStartWidth) > 2 || Math.Abs(Height - _resizeStartHeight) > 2)
        {
            _imageData.PushUndoSnapshotExplicit(_imageData.X, _imageData.Y, _resizeStartWidth, _resizeStartHeight, _imageData.Opacity);
        }
        SavePosition();
        e.Handled = true;
    }

    private void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isResizing) return;

        var pos = e.GetPosition(this);
        var dx = pos.X - _resizeStart.X;
        var dy = pos.Y - _resizeStart.Y;

        double screenW = SystemParameters.PrimaryScreenWidth > 0 ? SystemParameters.PrimaryScreenWidth : 1920;
        double screenH = SystemParameters.PrimaryScreenHeight > 0 ? SystemParameters.PrimaryScreenHeight : 1080;
        double maxW = Math.Max(50, screenW - Left);
        double maxH = Math.Max(50, screenH - Top);

        var newWidth = Math.Clamp(_resizeStartWidth + dx, 50, maxW);
        double newHeight;
        if (_imageData.MaintainAspectRatio && _aspectRatio > 0)
        {
            newHeight = newWidth / _aspectRatio;
            if (newHeight > maxH)
            {
                newHeight = maxH;
                newWidth = Math.Clamp(newHeight * _aspectRatio, 50, maxW);
            }
            else if (newHeight < 50)
            {
                newHeight = 50;
                newWidth = Math.Clamp(newHeight * _aspectRatio, 50, maxW);
            }
        }
        else
        {
            newHeight = Math.Clamp(_resizeStartHeight + dy, 50, maxH);
        }

        Width = newWidth;
        Height = newHeight;
        _imageData.Width = newWidth;
        _imageData.Height = newHeight;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (_imageData != null && _imageData.CanUndo)
            {
                _imageData.PerformUndo();
                SavePosition();
                e.Handled = true;
            }
        }
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        _imageData.IsLocked = !_imageData.IsLocked;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        ImageRemoved?.Invoke(this, _imageData);
        Close();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_imageData != null)
        {
            _imageData.Opacity = e.NewValue;
            UpdateOpacity();
        }
    }

    private void SavePosition()
    {
        PositionChanged?.Invoke(this, _imageData);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        UpdateClickThrough();
    }

    protected override void OnClosed(EventArgs e)
    {
        _imageData.PropertyChanged -= ImageData_PropertyChanged;
        base.OnClosed(e);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    private void UpdateClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        if (_imageData.IsLocked)
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
        }
        else
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
        }
    }
}
