using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class ImagePreviewDialog : Window
    {
        private double _zoom = 1.0;
        private const double ZoomMin = 0.1;
        private const double ZoomMax = 5.0;
        private const double ZoomStep = 0.15;
        private bool _isPanning;
        private Point _panStart;
        private double _panStartH, _panStartV;

        public ImagePreviewDialog(string imagePath, string? title = null)
        {
            InitializeComponent();
            Title = title ?? "Image Preview";

            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(imagePath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    PreviewImage.Source = bmp;

                    var fi = new FileInfo(imagePath);
                    string size = fi.Length < 1024 * 1024
                        ? $"{fi.Length / 1024.0:F0} KB"
                        : $"{fi.Length / (1024.0 * 1024):F1} MB";
                    InfoLabel.Text = $"{bmp.PixelWidth} x {bmp.PixelHeight} px  |  {size}  |  {Path.GetFileName(imagePath)}";

                    // Auto-fit after layout is ready
                    Loaded += (_, _) => ZoomFit(null!, null!);
                }
                catch
                {
                    InfoLabel.Text = "Failed to load image";
                }
            }
            else
            {
                InfoLabel.Text = "Image not found";
            }
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                double delta = e.Delta > 0 ? ZoomStep : -ZoomStep;
                if (_zoom < 0.5) delta *= 0.5;
                SetZoom(_zoom + delta);
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                ScrollArea.ScrollToHorizontalOffset(ScrollArea.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                _isPanning = true;
                _panStart = e.GetPosition(ScrollArea);
                _panStartH = ScrollArea.HorizontalOffset;
                _panStartV = ScrollArea.VerticalOffset;
                ScrollArea.Cursor = Cursors.ScrollAll;
                ScrollArea.CaptureMouse();
                e.Handled = true;
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && _isPanning)
            {
                _isPanning = false;
                ScrollArea.Cursor = null;
                ScrollArea.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                var pos = e.GetPosition(ScrollArea);
                ScrollArea.ScrollToHorizontalOffset(_panStartH + (_panStart.X - pos.X));
                ScrollArea.ScrollToVerticalOffset(_panStartV + (_panStart.Y - pos.Y));
                e.Handled = true;
            }
        }

        private void ZoomIn(object sender, RoutedEventArgs e) => SetZoom(_zoom + ZoomStep);
        private void ZoomOut(object sender, RoutedEventArgs e) => SetZoom(_zoom - ZoomStep);
        private void ZoomReset(object sender, RoutedEventArgs e) => SetZoom(1.0);

        private void ZoomFit(object sender, RoutedEventArgs e)
        {
            if (PreviewImage.Source is BitmapImage bmp && bmp.PixelWidth > 0)
            {
                // Use DIP dimensions (bmp.Width/Height) not raw pixels — accounts for source DPI
                double fitW = (ScrollArea.ViewportWidth - 20) / bmp.Width;
                double fitH = (ScrollArea.ViewportHeight - 20) / bmp.Height;
                SetZoom(Math.Min(fitW, fitH));
            }
        }

        private void SetZoom(double zoom)
        {
            _zoom = Math.Clamp(zoom, ZoomMin, ZoomMax);
            ImageScale.ScaleX = _zoom;
            ImageScale.ScaleY = _zoom;
            ZoomLabel.Text = $"{(int)(_zoom * 100)}%";
        }
    }
}
