using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using SkiaSharp;

namespace MTGProxyBuilder.UI.Controls
{
    public class CardEditorCanvas : Border
    {
        private readonly Image _image;
        private readonly CardCompositor _compositor = new();
        private readonly DispatcherTimer _redrawTimer;
        private WriteableBitmap? _bitmap;

        private readonly List<LayerBase> _subscribedLayers = new();

        // Drag state
        private bool _isDragging;
        private Point _dragStart;
        private float _dragLayerStartX;
        private float _dragLayerStartY;
        private IPointer? _capturedPointer;

        public CardEditorCanvas()
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
            ClipToBounds = true;

            _image = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Child = _image;

            _redrawTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _redrawTimer.Tick += (_, _) => { _redrawTimer.Stop(); Render(); };

            SizeChanged += (_, _) => QueueRedraw();
        }

        // ================================================================
        //  STYLED PROPERTIES
        // ================================================================

        public static readonly StyledProperty<CustomCardProject?> ProjectProperty =
            AvaloniaProperty.Register<CardEditorCanvas, CustomCardProject?>(nameof(Project));

        public static readonly StyledProperty<LayerBase?> SelectedLayerProperty =
            AvaloniaProperty.Register<CardEditorCanvas, LayerBase?>(nameof(SelectedLayer),
                defaultBindingMode: BindingMode.TwoWay);

        public static readonly StyledProperty<double> ZoomProperty =
            AvaloniaProperty.Register<CardEditorCanvas, double>(nameof(Zoom), defaultValue: 1.0);

        public static readonly StyledProperty<int> RefreshTriggerProperty =
            AvaloniaProperty.Register<CardEditorCanvas, int>(nameof(RefreshTrigger));

        public CustomCardProject? Project       { get => GetValue(ProjectProperty);       set => SetValue(ProjectProperty, value); }
        public LayerBase?         SelectedLayer { get => GetValue(SelectedLayerProperty); set => SetValue(SelectedLayerProperty, value); }
        public double             Zoom          { get => GetValue(ZoomProperty);          set => SetValue(ZoomProperty, value); }
        public int                RefreshTrigger{ get => GetValue(RefreshTriggerProperty);set => SetValue(RefreshTriggerProperty, value); }

        public event EventHandler<LayerBase?>? LayerSelected;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == ProjectProperty ||
                change.Property == SelectedLayerProperty ||
                change.Property == ZoomProperty ||
                change.Property == RefreshTriggerProperty)
            {
                if (change.Property == ProjectProperty)
                    SubscribeToLayers();
                QueueRedraw();
            }
        }

        private void SubscribeToLayers()
        {
            foreach (var layer in _subscribedLayers)
                layer.PropertyChanged -= OnLayerPropertyChanged;
            _subscribedLayers.Clear();

            var project = Project;
            if (project == null) return;
            foreach (var layer in project.Layers)
            {
                layer.PropertyChanged += OnLayerPropertyChanged;
                _subscribedLayers.Add(layer);
            }
        }

        private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e) => QueueRedraw();

        // ================================================================
        //  RENDERING
        // ================================================================

        public void QueueRedraw()
        {
            _redrawTimer.Stop();
            _redrawTimer.Start();
        }

        private void Render()
        {
            var project = Project;
            if (project == null) return;

            int viewWidth  = (int)Bounds.Width;
            int viewHeight = (int)Bounds.Height;
            if (viewWidth < 1 || viewHeight < 1) return;

            using var skBitmap = _compositor.RenderPreview(project, viewWidth, viewHeight);

            if (SelectedLayer != null && SelectedLayer.IsVisible)
            {
                using var overlayCanvas = new SKCanvas(skBitmap);
                float scaleX = (float)skBitmap.Width  / project.CardWidthPx;
                float scaleY = (float)skBitmap.Height / project.CardHeightPx;
                float scale  = Math.Min(scaleX, scaleY);
                overlayCanvas.Save();
                overlayCanvas.Scale(scale);
                DrawSelectionHandle(overlayCanvas, SelectedLayer);
                overlayCanvas.Restore();
            }

            CopyToWriteableBitmap(skBitmap);
        }

        private static void DrawSelectionHandle(SKCanvas canvas, LayerBase layer)
        {
            using var paint = new SKPaint
            {
                Color = new SKColor(0x1E, 0x90, 0xFF, 0xCC),
                Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true,
                PathEffect = SKPathEffect.CreateDash(new[] { 6f, 4f }, 0)
            };
            canvas.Save();
            canvas.Translate(layer.X, layer.Y);
            if (layer.Rotation != 0)
            {
                canvas.Translate(layer.Width / 2, layer.Height / 2);
                canvas.RotateDegrees(layer.Rotation);
                canvas.Translate(-layer.Width / 2, -layer.Height / 2);
            }
            canvas.DrawRect(0, 0, layer.Width, layer.Height, paint);

            float handleSize = 6;
            using var handlePaint  = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
            using var handleBorder = new SKPaint { Color = new SKColor(0x1E, 0x90, 0xFF), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
            foreach (var corner in new SKPoint[] { new(0, 0), new(layer.Width, 0), new(0, layer.Height), new(layer.Width, layer.Height) })
            {
                canvas.DrawRect(corner.X - handleSize / 2, corner.Y - handleSize / 2, handleSize, handleSize, handlePaint);
                canvas.DrawRect(corner.X - handleSize / 2, corner.Y - handleSize / 2, handleSize, handleSize, handleBorder);
            }
            canvas.Restore();
        }

        private void CopyToWriteableBitmap(SKBitmap skBitmap)
        {
            int w = skBitmap.Width, h = skBitmap.Height;

            if (_bitmap == null || _bitmap.PixelSize.Width != w || _bitmap.PixelSize.Height != h)
            {
                _bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
                _image.Source = _bitmap;
            }

            var srcPtr = skBitmap.GetPixels();
            using var fb = _bitmap.Lock();
            unsafe
            {
                long size = (long)fb.RowBytes * h;
                Buffer.MemoryCopy(srcPtr.ToPointer(), fb.Address.ToPointer(), size, size);
            }
        }

        // ================================================================
        //  POINTER EVENTS
        // ================================================================

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            var project = Project;
            if (project == null) return;

            var pos     = e.GetPosition(_image);
            var cardPos = ScreenToCard(pos);

            var hitLayer = project.Layers
                .Where(l => l.IsVisible && !l.IsLocked)
                .OrderByDescending(l => l.ZOrder)
                .FirstOrDefault(l => HitTestLayer(l, cardPos));

            SelectedLayer = hitLayer;
            LayerSelected?.Invoke(this, hitLayer);

            if (hitLayer != null)
            {
                _isDragging = true;
                _dragStart = pos;
                _dragLayerStartX = hitLayer.X;
                _dragLayerStartY = hitLayer.Y;
                _capturedPointer = e.Pointer;
                e.Pointer.Capture(this);
            }
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_isDragging || SelectedLayer == null || Project == null || _capturedPointer == null) return;

            var pos   = e.GetPosition(_image);
            var scale = GetCardScale();
            float dx  = (float)((pos.X - _dragStart.X) / scale);
            float dy  = (float)((pos.Y - _dragStart.Y) / scale);

            SelectedLayer.X = _dragLayerStartX + dx;
            SelectedLayer.Y = _dragLayerStartY + dy;
            QueueRedraw();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!_isDragging) return;
            _isDragging = false;
            _capturedPointer?.Capture(null);
            _capturedPointer = null;
        }

        // ================================================================
        //  HELPERS
        // ================================================================

        private Point ScreenToCard(Point screenPos)
        {
            var scale = GetCardScale();
            return new Point(screenPos.X / scale, screenPos.Y / scale);
        }

        private double GetCardScale()
        {
            var project = Project;
            if (project == null || _image.Bounds.Width < 1) return 1;
            double scaleX = _image.Bounds.Width  / project.CardWidthPx;
            double scaleY = _image.Bounds.Height / project.CardHeightPx;
            return Math.Min(scaleX, scaleY);
        }

        private static bool HitTestLayer(LayerBase layer, Point cardPos) =>
            cardPos.X >= layer.X && cardPos.X <= layer.X + layer.Width
         && cardPos.Y >= layer.Y && cardPos.Y <= layer.Y + layer.Height;

        public CardCompositor Compositor => _compositor;
    }
}
