using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace MTGProxyBuilder.UI.Controls
{
    public partial class ImagePreviewPanel : UserControl
    {
        private double _zoom = 1.0;
        private const double ZoomMin = 0.1;
        private const double ZoomMax = 5.0;
        private const double ZoomStep = 0.15;
        private bool _isPanning;
        private Point _panStart;
        private double _panStartH, _panStartV;

        public ImagePreviewPanel()
        {
            InitializeComponent();
        }

        /// <summary>Display a pre-loaded BitmapImage with title and detail text.</summary>
        public void ShowImage(BitmapImage image, string title, string detail)
        {
            PreviewImage.Source = image;
            TitleLabel.Text = title;
            DetailLabel.Text = detail;
            ZoomToFit(image);
        }

        /// <summary>Load an image from a file path, compute metadata, and display it.</summary>
        public void ShowImage(string filePath, string title, string? detail = null)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(filePath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                // Calculate DPI assuming 63x88mm MTG card
                double widthInches = 63.0 / 25.4;
                double heightInches = 88.0 / 25.4;
                int dpiW = (int)(bmp.PixelWidth / widthInches);
                int dpiH = (int)(bmp.PixelHeight / heightInches);
                int dpi = Math.Min(dpiW, dpiH);

                var fi = new FileInfo(filePath);
                string size = fi.Length < 1024 * 1024
                    ? $"{fi.Length / 1024.0:F0} KB"
                    : $"{fi.Length / (1024.0 * 1024):F1} MB";

                string metaLine = $"{bmp.PixelWidth} x {bmp.PixelHeight} px  |  ~{dpi} DPI  |  {size}";
                string fullDetail = string.IsNullOrEmpty(detail)
                    ? metaLine
                    : $"{metaLine}\n{detail}";

                ShowImage(bmp, title, fullDetail);
            }
            catch
            {
                PreviewImage.Source = null;
                TitleLabel.Text = title;
                DetailLabel.Text = "Failed to load image";
            }
        }

        /// <summary>Reset the panel to its empty state.</summary>
        public void Clear()
        {
            PreviewImage.Source = null;
            TitleLabel.Text = "Select an image";
            DetailLabel.Text = "";
            SetZoom(1.0);
        }

        /// <summary>Fit the current image to the viewport.</summary>
        public void ZoomToFit(BitmapImage? bmp = null)
        {
            bmp ??= PreviewImage.Source as BitmapImage;
            if (bmp == null || bmp.PixelWidth == 0) return;

            double fitW = (ScrollArea.ViewportWidth - 20) / bmp.Width;
            double fitH = (ScrollArea.ViewportHeight - 20) / bmp.Height;
            double fit = Math.Min(fitW, fitH);
            if (fit <= 0) fit = 0.5;
            SetZoom(fit);
        }

        private void SetZoom(double zoom)
        {
            _zoom = Math.Clamp(zoom, ZoomMin, ZoomMax);
            ImageScale.ScaleX = _zoom;
            ImageScale.ScaleY = _zoom;
            ZoomLabel.Text = $"{(int)(_zoom * 100)}%";
        }

        // --- Mouse handlers ---

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

        // --- Zoom button handlers ---

        private void OnZoomIn(object sender, RoutedEventArgs e) => SetZoom(_zoom + ZoomStep);
        private void OnZoomOut(object sender, RoutedEventArgs e) => SetZoom(_zoom - ZoomStep);
        private void OnZoomReset(object sender, RoutedEventArgs e) => SetZoom(1.0);
        private void OnZoomFit(object sender, RoutedEventArgs e) => ZoomToFit();
    }
}
