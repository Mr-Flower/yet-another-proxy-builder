using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

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
        private IPointer? _panPointer;

        // Natural size of the currently loaded image (in pixels)
        private double _naturalW;
        private double _naturalH;
        // True while the image is auto-fitted to the viewport; manual zoom turns it off.
        // Keeps the card fully visible and at a constant size as the panel/window resizes.
        private bool _fitMode = true;

        public ImagePreviewPanel()
        {
            InitializeComponent();

            ScrollArea.PointerWheelChanged += OnMouseWheel;
            ScrollArea.PointerPressed += OnPointerPressed;
            ScrollArea.PointerReleased += OnPointerReleased;
            ScrollArea.PointerMoved += OnPointerMoved;
            // Re-fit when the preview area resizes (window widened / splitter dragged) so the whole
            // card stays visible without manual zooming.
            ScrollArea.SizeChanged += (_, _) => { if (_fitMode) ZoomToFit(); };
        }

        /// <summary>Display a pre-loaded Bitmap with title and detail text.</summary>
        public void ShowImage(Bitmap image, string title, string detail)
        {
            _naturalW = image.PixelSize.Width;
            _naturalH = image.PixelSize.Height;
            PreviewImage.Source = image;
            TitleLabel.Text = title;
            DetailLabel.Text = detail;
            _fitMode = true;
            // Defer until the panel has a measured viewport; on first show the viewport may still be
            // 0, which would otherwise fit against a zero size and leave the image overflowing.
            ZoomToFit(image);
            Dispatcher.UIThread.Post(() => { if (_fitMode) ZoomToFit(image); }, DispatcherPriority.Loaded);
        }

        /// <summary>Load an image from a file path, compute metadata, and display it.</summary>
        public void ShowImage(string filePath, string title, string? detail = null)
        {
            try
            {
                var bmp = new Bitmap(filePath);

                // Calculate DPI assuming 63x88mm MTG card
                double widthInches = 63.0 / 25.4;
                double heightInches = 88.0 / 25.4;
                int dpiW = (int)(bmp.PixelSize.Width / widthInches);
                int dpiH = (int)(bmp.PixelSize.Height / heightInches);
                int dpi = Math.Min(dpiW, dpiH);

                var fi = new FileInfo(filePath);
                string size = fi.Length < 1024 * 1024
                    ? $"{fi.Length / 1024.0:F0} KB"
                    : $"{fi.Length / (1024.0 * 1024):F1} MB";

                string metaLine = $"{bmp.PixelSize.Width} x {bmp.PixelSize.Height} px  |  ~{dpi} DPI  |  {size}";
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
            _naturalW = 0;
            _naturalH = 0;
            TitleLabel.Text = "Select an image";
            DetailLabel.Text = "";
            SetZoom(1.0);
        }

        /// <summary>Fit the current image to the viewport.</summary>
        public void ZoomToFit(Bitmap? bmp = null)
        {
            bmp ??= PreviewImage.Source as Bitmap;
            if (bmp == null || bmp.PixelSize.Width == 0) return;

            // Fall back to the control bounds when the viewport hasn't been measured yet.
            double availW = ScrollArea.Viewport.Width > 0 ? ScrollArea.Viewport.Width : ScrollArea.Bounds.Width;
            double availH = ScrollArea.Viewport.Height > 0 ? ScrollArea.Viewport.Height : ScrollArea.Bounds.Height;
            if (availW <= 0 || availH <= 0) return;

            double fitW = (availW - 20) / bmp.Size.Width;
            double fitH = (availH - 20) / bmp.Size.Height;
            double fit = Math.Min(fitW, fitH);
            if (fit <= 0) fit = 0.5;
            _fitMode = true;
            SetZoom(fit);
        }

        private void SetZoom(double zoom)
        {
            _zoom = Math.Clamp(zoom, ZoomMin, ZoomMax);
            if (_naturalW > 0 && _naturalH > 0)
            {
                PreviewImage.Width = _naturalW * _zoom;
                PreviewImage.Height = _naturalH * _zoom;
            }
            ZoomLabel.Text = $"{(int)(_zoom * 100)}%";
        }

        // --- Pointer / scroll handlers ---

        private void OnMouseWheel(object? sender, PointerWheelEventArgs e)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                double delta = e.Delta.Y > 0 ? ZoomStep : -ZoomStep;
                if (_zoom < 0.5) delta *= 0.5;
                _fitMode = false;
                SetZoom(_zoom + delta);
                e.Handled = true;
                return;
            }

            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                ScrollArea.Offset = new Vector(ScrollArea.Offset.X - e.Delta.Y * 50, ScrollArea.Offset.Y);
                e.Handled = true;
            }
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(ScrollArea).Properties;
            if (props.IsMiddleButtonPressed)
            {
                _isPanning = true;
                _panStart = e.GetPosition(ScrollArea);
                _panStartH = ScrollArea.Offset.X;
                _panStartV = ScrollArea.Offset.Y;
                ScrollArea.Cursor = new Cursor(StandardCursorType.SizeAll);
                _panPointer = e.Pointer;
                e.Pointer.Capture(ScrollArea);
                e.Handled = true;
            }
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_isPanning && e.Pointer == _panPointer)
            {
                _isPanning = false;
                ScrollArea.Cursor = null;
                e.Pointer.Capture(null);
                _panPointer = null;
                e.Handled = true;
            }
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_isPanning)
            {
                var pos = e.GetPosition(ScrollArea);
                ScrollArea.Offset = new Vector(
                    _panStartH + (_panStart.X - pos.X),
                    _panStartV + (_panStart.Y - pos.Y));
                e.Handled = true;
            }
        }

        // --- Zoom button handlers ---

        private void OnZoomIn(object? sender, RoutedEventArgs e) { _fitMode = false; SetZoom(_zoom + ZoomStep); }
        private void OnZoomOut(object? sender, RoutedEventArgs e) { _fitMode = false; SetZoom(_zoom - ZoomStep); }
        private void OnZoomReset(object? sender, RoutedEventArgs e) { _fitMode = false; SetZoom(1.0); }
        private void OnZoomFit(object? sender, RoutedEventArgs e) => ZoomToFit();
    }
}
