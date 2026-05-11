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
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || true) // always zoom on scroll
            {
                double delta = e.Delta > 0 ? ZoomStep : -ZoomStep;
                if (_zoom < 0.5) delta *= 0.5;
                SetZoom(_zoom + delta);
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
                double fitW = (ScrollArea.ViewportWidth - 20) / bmp.PixelWidth;
                double fitH = (ScrollArea.ViewportHeight - 20) / bmp.PixelHeight;
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
