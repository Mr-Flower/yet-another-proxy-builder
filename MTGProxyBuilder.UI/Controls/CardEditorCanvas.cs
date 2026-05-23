using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using SkiaSharp;

namespace MTGProxyBuilder.UI.Controls
{
    public class CardEditorCanvas : Border
    {
        private readonly System.Windows.Controls.Image _image;
        private readonly CardCompositor _compositor = new();
        private readonly DispatcherTimer _redrawTimer;
        private WriteableBitmap? _bitmap;

        // Layer property change tracking
        private readonly List<LayerBase> _subscribedLayers = new();

        // Drag state
        private bool _isDragging;
        private Point _dragStart;
        private float _dragLayerStartX;
        private float _dragLayerStartY;

        public CardEditorCanvas()
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
            ClipToBounds = true;

            _image = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Child = _image;

            _redrawTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(80)
            };
            _redrawTimer.Tick += (_, _) =>
            {
                _redrawTimer.Stop();
                Render();
            };
        }

        // --- Dependency Properties ---

        public static readonly DependencyProperty ProjectProperty =
            DependencyProperty.Register(nameof(Project), typeof(CustomCardProject), typeof(CardEditorCanvas),
                new PropertyMetadata(null, OnProjectChanged));

        public CustomCardProject? Project
        {
            get => (CustomCardProject?)GetValue(ProjectProperty);
            set => SetValue(ProjectProperty, value);
        }

        public static readonly DependencyProperty SelectedLayerProperty =
            DependencyProperty.Register(nameof(SelectedLayer), typeof(LayerBase), typeof(CardEditorCanvas),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRenderPropertyChanged));

        public LayerBase? SelectedLayer
        {
            get => (LayerBase?)GetValue(SelectedLayerProperty);
            set => SetValue(SelectedLayerProperty, value);
        }

        public static readonly DependencyProperty ZoomProperty =
            DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(CardEditorCanvas),
                new PropertyMetadata(1.0, OnRenderPropertyChanged));

        public double Zoom
        {
            get => (double)GetValue(ZoomProperty);
            set => SetValue(ZoomProperty, value);
        }

        public static readonly DependencyProperty RefreshTriggerProperty =
            DependencyProperty.Register(nameof(RefreshTrigger), typeof(int), typeof(CardEditorCanvas),
                new PropertyMetadata(0, OnRenderPropertyChanged));

        public int RefreshTrigger
        {
            get => (int)GetValue(RefreshTriggerProperty);
            set => SetValue(RefreshTriggerProperty, value);
        }

        // --- Events ---

        public event EventHandler<LayerBase?>? LayerSelected;

        // --- Property Changed Handlers ---

        private static void OnProjectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CardEditorCanvas canvas)
            {
                canvas.SubscribeToLayers();
                canvas.QueueRedraw();
            }
        }

        private static void OnRenderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CardEditorCanvas canvas)
            {
                canvas.SubscribeToLayers();
                canvas.QueueRedraw();
            }
        }

        private void SubscribeToLayers()
        {
            // Unsubscribe from old layers
            foreach (var layer in _subscribedLayers)
                layer.PropertyChanged -= OnLayerPropertyChanged;
            _subscribedLayers.Clear();

            // Subscribe to current layers
            var project = Project;
            if (project == null) return;

            foreach (var layer in project.Layers)
            {
                layer.PropertyChanged += OnLayerPropertyChanged;
                _subscribedLayers.Add(layer);
            }
        }

        private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            QueueRedraw();
        }

        // --- Rendering ---

        public void QueueRedraw()
        {
            _redrawTimer.Stop();
            _redrawTimer.Start();
        }

        private void Render()
        {
            var project = Project;
            if (project == null) return;

            int viewWidth = (int)ActualWidth;
            int viewHeight = (int)ActualHeight;
            if (viewWidth < 1 || viewHeight < 1) return;

            // Render the card preview
            using var skBitmap = _compositor.RenderPreview(project, viewWidth, viewHeight);

            // Draw selection overlay onto the preview
            if (SelectedLayer != null && SelectedLayer.IsVisible)
            {
                using var overlayCanvas = new SKCanvas(skBitmap);
                float scaleX = (float)skBitmap.Width / project.CardWidthPx;
                float scaleY = (float)skBitmap.Height / project.CardHeightPx;
                float scale = Math.Min(scaleX, scaleY);

                overlayCanvas.Save();
                overlayCanvas.Scale(scale);
                DrawSelectionHandle(overlayCanvas, SelectedLayer);
                overlayCanvas.Restore();
            }

            // Copy SkiaSharp bitmap to WriteableBitmap
            CopyToWriteableBitmap(skBitmap);
        }

        private void DrawSelectionHandle(SKCanvas canvas, LayerBase layer)
        {
            using var paint = new SKPaint
            {
                Color = new SKColor(0x1E, 0x90, 0xFF, 0xCC),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                IsAntialias = true,
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

            // Draw corner handles
            float handleSize = 6;
            using var handlePaint = new SKPaint
            {
                Color = SKColors.White,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            using var handleBorder = new SKPaint
            {
                Color = new SKColor(0x1E, 0x90, 0xFF),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                IsAntialias = true
            };

            var corners = new SKPoint[]
            {
                new(0, 0),
                new(layer.Width, 0),
                new(0, layer.Height),
                new(layer.Width, layer.Height)
            };

            foreach (var corner in corners)
            {
                canvas.DrawRect(corner.X - handleSize / 2, corner.Y - handleSize / 2, handleSize, handleSize, handlePaint);
                canvas.DrawRect(corner.X - handleSize / 2, corner.Y - handleSize / 2, handleSize, handleSize, handleBorder);
            }

            canvas.Restore();
        }

        private void CopyToWriteableBitmap(SKBitmap skBitmap)
        {
            int w = skBitmap.Width;
            int h = skBitmap.Height;

            if (_bitmap == null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
            {
                _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                _image.Source = _bitmap;
            }

            _bitmap.Lock();
            try
            {
                var srcPtr = skBitmap.GetPixels();
                long size = (long)skBitmap.RowBytes * h;
                unsafe
                {
                    Buffer.MemoryCopy(srcPtr.ToPointer(), _bitmap.BackBuffer.ToPointer(), size, size);
                }
                _bitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
            }
            finally
            {
                _bitmap.Unlock();
            }
        }

        // --- Mouse Interaction ---

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            var project = Project;
            if (project == null) return;

            var pos = e.GetPosition(_image);
            var cardPos = ScreenToCard(pos);

            // Hit test layers in reverse Z-order (top first)
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
                CaptureMouse();
            }

            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (!_isDragging || SelectedLayer == null || Project == null) return;

            var pos = e.GetPosition(_image);
            var scale = GetCardScale();

            float dx = (float)((pos.X - _dragStart.X) / scale);
            float dy = (float)((pos.Y - _dragStart.Y) / scale);

            SelectedLayer.X = _dragLayerStartX + dx;
            SelectedLayer.Y = _dragLayerStartY + dy;

            QueueRedraw();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            QueueRedraw();
        }

        // --- Helpers ---

        private Point ScreenToCard(Point screenPos)
        {
            var project = Project;
            if (project == null) return screenPos;

            var scale = GetCardScale();
            return new Point(screenPos.X / scale, screenPos.Y / scale);
        }

        private double GetCardScale()
        {
            var project = Project;
            if (project == null || _image.ActualWidth < 1) return 1;

            double scaleX = _image.ActualWidth / project.CardWidthPx;
            double scaleY = _image.ActualHeight / project.CardHeightPx;
            return Math.Min(scaleX, scaleY);
        }

        private static bool HitTestLayer(LayerBase layer, Point cardPos)
        {
            // Simple AABB hit test (ignores rotation for now)
            return cardPos.X >= layer.X && cardPos.X <= layer.X + layer.Width
                && cardPos.Y >= layer.Y && cardPos.Y <= layer.Y + layer.Height;
        }

        /// <summary>
        /// Access the compositor for image cache invalidation.
        /// </summary>
        public CardCompositor Compositor => _compositor;
    }
}
