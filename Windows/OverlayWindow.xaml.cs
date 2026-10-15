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
            _aspectRatio = bitmap.PixelWidth / (double)bitmap.PixelHeight;

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
        
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Ignore if mouse button state changed
        }

        _isUpdatingFromWindow = true;
        try
        {
            _imageData.X = Left;
            _imageData.Y = Top;
            SavePosition();
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
        if (_imageData == null || _imageData.IsLocked) return;

        var mousePos = e.GetPosition(this);
        double mouseX = mousePos.X;
        double mouseY = mousePos.Y;

        var delta = e.Delta > 0 ? 1.05 : 0.95;
        var newWidth = Width * delta;
        var newHeight = _imageData.MaintainAspectRatio 
            ? newWidth / _aspectRatio 
            : Height * delta;

        // Anchor calculations to scale centered on the mouse cursor
        double x_rel = mouseX / Width;
        double y_rel = mouseY / Height;

        _isUpdatingFromWindow = true;
        try
        {
            Left = Left - (newWidth - Width) * x_rel;
            Top = Top - (newHeight - Height) * y_rel;
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
        e.Handled = true;
    }

    private void ResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isResizing = false;
        SavePosition();
        e.Handled = true;
    }

    private void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isResizing) return;

        var pos = e.GetPosition(this);
        var dx = pos.X - _resizeStart.X;
        var dy = pos.Y - _resizeStart.Y;

        var newWidth = Math.Max(50, _resizeStartWidth + dx);
        var newHeight = _imageData.MaintainAspectRatio
            ? newWidth / _aspectRatio
            : Math.Max(50, _resizeStartHeight + dy);

        Width = newWidth;
        Height = newHeight;
        _imageData.Width = newWidth;
        _imageData.Height = newHeight;
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
