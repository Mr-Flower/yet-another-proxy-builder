using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.Controls
{
    // Retained-mode editor surface: a single Render(DrawingContext) paints every page, card, guide and
    // overlay from cached state. Previously this materialised hundreds of Avalonia controls per redraw;
    // drawing directly removes that churn and the layout work that came with it. Selection, the drop
    // highlight and the drag ghost are state that Render draws, updated with InvalidateVisual().
    public class GridEditorCanvas : Canvas
    {
        private const float MmToPx = 96f / 25.4f;
        private const float PageGapPx = 30f;

        // Drag state
        private bool _isDragging;
        private int _dragSourceCardIndex = -1;
        private int _pendingSelectSlot = -1;
        private Point _dragStart;
        private Point _dragGhostCenter;
        private Bitmap? _dragGhostBitmap;
        private int _dropHighlightSlot = -1;

        // Pointer capture
        private IPointer? _capturedPointer;

        // Selection state
        private readonly HashSet<int> _selectedSlots = new();
        private int _lastSelectedSlot = -1;

        // Flip state
        private readonly HashSet<int> _flippedCardIndices = new();
        private bool _allFlipped;

        // Cached layout info for hit testing and rendering
        private float _pageW, _pageH, _cellW, _cellH, _marginL, _marginT, _marginR, _marginB;
        private float _bleed, _cardW, _cardH;
        private int _cols, _rows, _perPage, _totalPages;
        private bool _regMarksActive;
        private List<ExpandedSlot> _expandedSlots = new();

        // Image cache. Shared (static) across all canvas instances and bounded FIFO: without a cap it grew
        // for the whole session. Evicted entries are only dropped from the dictionary, never Disposed —
        // a bitmap may still be drawn in the current frame, so we let the GC reclaim it once it is no
        // longer referenced.
        private const int MaxCachedImages = 512;
        private static readonly ConcurrentDictionary<string, Bitmap?> _imageCache = new();
        private static readonly ConcurrentQueue<string> _imageCacheOrder = new();

        // Bleed-extended images for a WYSIWYG preview matching the PDF. The bled images are produced
        // and disk-cached by BleedProcessor (shared with PDF export); here we additionally map
        // (sourcePath|bleedMm|useBleed) -> display path so the card painter can look them up cheaply.
        private static readonly BleedProcessor _bleedProcessor = new();
        private static readonly ConcurrentDictionary<string, string> _displayPathCache = new();
        private double _bleedMm;
        private bool _useBleed;
        private bool _processBleed;
        private double _cardWmm;
        private static string DisplayKey(string raw, double bleedMm, bool useBleed, double cardWmm)
            => $"{raw}|{bleedMm}|{useBleed}|{cardWmm}";

        // Reusable brushes/pens for the static chrome.
        private static readonly IBrush PageFill = Brushes.White;
        private static readonly Pen PagePen = new(Brushes.Black, 0.5);
        private static readonly Pen MarginPen = new(Brushes.LightBlue, 0.5, new DashStyle(new double[] { 4, 4 }, 0));
        private static readonly IBrush SlotFill = new SolidColorBrush(Color.FromArgb(15, 0, 0, 0));
        private static readonly Pen SlotPen = new(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), 0.5, new DashStyle(new double[] { 2, 2 }, 0));
        private static readonly IBrush PageNumBrush = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
        private static readonly Pen SelectionPen = new(Brushes.DodgerBlue, 3);
        private static readonly IBrush DropFill = new SolidColorBrush(Color.FromArgb(50, 0, 120, 255));
        private static readonly Pen DropPen = new(Brushes.DodgerBlue, 2);
        private static readonly IBrush GhostFill = new SolidColorBrush(Color.FromArgb(180, 70, 130, 200));
        private static readonly Pen GhostPen = new(Brushes.DodgerBlue, 2);

        // Debounce + async image load
        private DispatcherTimer? _redrawTimer;
        private CancellationTokenSource? _redrawCts;

        private record ExpandedSlot(CardModel Card, int CardIndex);

        public GridEditorCanvas()
        {
            Background = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
            ClipToBounds = true;
            Focusable = true;
            // Stop the parent ScrollViewer from scrolling the (huge) canvas to its top when it gains
            // keyboard focus on click — that was the "scroll, then click jumps back to the start" bug.
            ScrollViewer.SetBringIntoViewOnFocusChange(this, false);
            AddHandler(Control.RequestBringIntoViewEvent, (_, e) => e.Handled = true);

            _redrawTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _redrawTimer.Tick += (_, _) => { _redrawTimer.Stop(); RedrawSafe(); };

            ContextRequested += OnContextRequested;
        }

        // Detach all model subscriptions and stop the timer when the control leaves the visual tree
        // (e.g. its project tab is closed). Without this the bound PageSettings/PrintSettings/Cards keep
        // a reference to a dead canvas, leaking it and triggering redraws on a control nobody can see.
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            if (PageSettings is { } ps) ps.PropertyChanged -= OnSettingsPropChanged;
            if (PrintSettingsSource is { } prs) prs.PropertyChanged -= OnPrintSettingsPropChanged;
            if (CardsSource is { } cards) cards.CollectionChanged -= OnCollectionChanged;

            _redrawTimer?.Stop();
            _redrawCts?.Cancel();
        }

        // ================================================================
        //  STYLED PROPERTIES
        // ================================================================

        public static readonly StyledProperty<PageLayout?> PageSettingsProperty =
            AvaloniaProperty.Register<GridEditorCanvas, PageLayout?>(nameof(PageSettings));

        public static readonly StyledProperty<ObservableCollection<CardModel>?> CardsSourceProperty =
            AvaloniaProperty.Register<GridEditorCanvas, ObservableCollection<CardModel>?>(nameof(CardsSource));

        public static readonly StyledProperty<bool> ShowCutGuidesProperty =
            AvaloniaProperty.Register<GridEditorCanvas, bool>(nameof(ShowCutGuides), defaultValue: true);

        public static readonly StyledProperty<PrintSettings?> PrintSettingsSourceProperty =
            AvaloniaProperty.Register<GridEditorCanvas, PrintSettings?>(nameof(PrintSettingsSource));

        public static readonly StyledProperty<string?> RenderProgressProperty =
            AvaloniaProperty.Register<GridEditorCanvas, string?>(nameof(RenderProgress));

        public static readonly StyledProperty<bool> IsRenderingProperty =
            AvaloniaProperty.Register<GridEditorCanvas, bool>(nameof(IsRendering));

        public static readonly StyledProperty<CardModel?> SelectedCardProperty =
            AvaloniaProperty.Register<GridEditorCanvas, CardModel?>(nameof(SelectedCard),
                defaultBindingMode: BindingMode.TwoWay);

        public static readonly StyledProperty<int> RefreshTriggerProperty =
            AvaloniaProperty.Register<GridEditorCanvas, int>(nameof(RefreshTrigger));

        public static readonly StyledProperty<UndoService?> UndoSvcProperty =
            AvaloniaProperty.Register<GridEditorCanvas, UndoService?>(nameof(UndoSvc));

        // ZoomLevel: multiplier applied to MmToPx so ScrollViewer sees the zoomed extent.
        public static readonly StyledProperty<double> ZoomLevelProperty =
            AvaloniaProperty.Register<GridEditorCanvas, double>(nameof(ZoomLevel), defaultValue: 1.0);

        public PageLayout?  PageSettings      { get => GetValue(PageSettingsProperty);      set => SetValue(PageSettingsProperty, value); }
        public ObservableCollection<CardModel>? CardsSource { get => GetValue(CardsSourceProperty); set => SetValue(CardsSourceProperty, value); }
        public bool         ShowCutGuides     { get => GetValue(ShowCutGuidesProperty);     set => SetValue(ShowCutGuidesProperty, value); }
        public PrintSettings? PrintSettingsSource { get => GetValue(PrintSettingsSourceProperty); set => SetValue(PrintSettingsSourceProperty, value); }
        public string?      RenderProgress    { get => GetValue(RenderProgressProperty);    set => SetValue(RenderProgressProperty, value); }
        public bool         IsRendering       { get => GetValue(IsRenderingProperty);       set => SetValue(IsRenderingProperty, value); }
        public CardModel?   SelectedCard      { get => GetValue(SelectedCardProperty);      set => SetValue(SelectedCardProperty, value); }
        public int          RefreshTrigger    { get => GetValue(RefreshTriggerProperty);    set => SetValue(RefreshTriggerProperty, value); }
        public UndoService? UndoSvc           { get => GetValue(UndoSvcProperty);           set => SetValue(UndoSvcProperty, value); }
        public double       ZoomLevel         { get => GetValue(ZoomLevelProperty);         set => SetValue(ZoomLevelProperty, value); }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == PageSettingsProperty)
            {
                if (change.OldValue is PageLayout old) old.PropertyChanged -= OnSettingsPropChanged;
                if (change.NewValue is PageLayout nw) nw.PropertyChanged += OnSettingsPropChanged;
                ScheduleRedraw();
            }
            else if (change.Property == CardsSourceProperty)
            {
                if (change.OldValue is ObservableCollection<CardModel> oldC) oldC.CollectionChanged -= OnCollectionChanged;
                if (change.NewValue is ObservableCollection<CardModel> newC) newC.CollectionChanged += OnCollectionChanged;
                _selectedSlots.Clear();
                ScheduleRedraw();
            }
            else if (change.Property == PrintSettingsSourceProperty)
            {
                if (change.OldValue is PrintSettings oldPs) oldPs.PropertyChanged -= OnPrintSettingsPropChanged;
                if (change.NewValue is PrintSettings newPs) newPs.PropertyChanged += OnPrintSettingsPropChanged;
                ScheduleRedraw();
            }
            else if (change.Property == ShowCutGuidesProperty ||
                     change.Property == RefreshTriggerProperty ||
                     change.Property == ZoomLevelProperty)
            {
                ScheduleRedraw();
            }
        }

        private void OnSettingsPropChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e) => ScheduleRedraw();
        private void OnPrintSettingsPropChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e) => ScheduleRedraw();
        private void OnCollectionChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => ScheduleRedraw();

        public event Action<CardModel, bool>? CardDoubleClicked;
        public event Action<CardModel>? CreateTokenRequested;
        public event Action<List<CardModel>>? CreateTokensFromCardsRequested;
        public event Action<List<int>>? ApplyMajorityBackRequested;
        public event Action<List<int>>? SelectFrontArtRequested;
        public event Action<List<int>>? SelectBackArtRequested;

        private void ScheduleRedraw()
        {
            if (_redrawTimer == null) return;
            _redrawTimer.Stop();
            _redrawTimer.Start();
        }

        // ================================================================
        //  STATE PREP + ASYNC IMAGE LOAD
        // ================================================================

        // Fire-and-forget entry point for the timer/flip callers: RedrawAsync runs background image work,
        // so an exception there would otherwise be swallowed by `_ = RedrawAsync()` and leave the canvas
        // stuck showing "Rendering...". Observe it, log it, and clear the busy state.
        private async void RedrawSafe()
        {
            try { await RedrawAsync(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Canvas redraw error: {ex.Message}");
                IsRendering = false;
                RenderProgress = null;
            }
        }

        private async Task RedrawAsync()
        {
            _redrawCts?.Cancel();
            _redrawCts = new CancellationTokenSource();
            var token = _redrawCts.Token;

            var settings = PageSettings;
            if (settings == null) { ClearContent(); return; }

            // Apply zoom by scaling the pixel-per-mm conversion factor.
            float mmPx = MmToPx * (float)ZoomLevel;

            float pageW  = settings.PageWidthMm  * mmPx;
            float pageH  = settings.PageHeightMm * mmPx;
            // Card-position adjustment nudges the whole grid on the sheet (printer offset compensation);
            // only the origin moves, so card spacing and bleed are unaffected. WYSIWYG with the PDF.
            float marginL = (settings.MarginLeftMm + settings.OffsetXmm) * mmPx;
            float marginT = (settings.MarginTopMm  + settings.OffsetYmm) * mmPx;
            float marginR = settings.MarginRightMm * mmPx;
            float marginB = settings.MarginBottomMm* mmPx;
            float effectiveBleedMm = settings.EffectiveBleedMm;
            float cellW   = (settings.CardWidthMm  + 2 * effectiveBleedMm) * mmPx;
            float cellH   = (settings.CardHeightMm + 2 * effectiveBleedMm) * mmPx;
            float bleed   = effectiveBleedMm * mmPx;
            float cardW   = settings.CardWidthMm   * mmPx;
            float cardH   = settings.CardHeightMm  * mmPx;

            bool regMarksActive = PrintSettingsSource?.ShowRegistrationMarks == true;
            _bleedMm = effectiveBleedMm;
            // Process (GetDisplayImage) whenever not in reg-marks mode, even at bleed 0 — MPCFill still
            // needs its native bleed trimmed. _useBleed only governs whether the bled image fills the cell.
            _processBleed = !regMarksActive;
            _useBleed = effectiveBleedMm > 0 && !regMarksActive;
            _cardWmm = settings.CardWidthMm;

            int cols = settings.CardsPerRow;
            int rows = settings.CardsPerColumn;
            int perPage  = settings.CardsPerPage;

            if (cols <= 0 || rows <= 0 || perPage <= 0) { ClearContent(); return; }

            var slots = BuildExpandedSlots();
            var pathsToLoad = CollectPathsToLoad(slots);

            if (pathsToLoad.Count > 0 && !await PreloadDisplayImagesAsync(pathsToLoad, (int)cellW * 2, token))
            {
                IsRendering = false;
                return;
            }
            if (token.IsCancellationRequested) { IsRendering = false; return; }

            _pageW = pageW; _pageH = pageH; _cellW = cellW; _cellH = cellH;
            _marginL = marginL; _marginT = marginT; _marginR = marginR; _marginB = marginB;
            _bleed = bleed; _cardW = cardW; _cardH = cardH;
            _cols = cols; _rows = rows; _perPage = perPage;
            _regMarksActive = regMarksActive;
            _expandedSlots = slots;

            int totalSlots = slots.Count;
            _totalPages = totalSlots > 0 ? (int)Math.Ceiling((double)totalSlots / perPage) : 1;
            if (_totalPages < 1) _totalPages = 1;

            _isDragging = false; _dragGhostBitmap = null; _dropHighlightSlot = -1;

            Width  = pageW;
            Height = _totalPages * pageH + (_totalPages - 1) * PageGapPx;

            IsRendering = false;
            RenderProgress = null;
            InvalidateVisual();
        }

        private void ClearContent()
        {
            _expandedSlots = new();
            _totalPages = 0;
            _cols = _rows = _perPage = 0;
            IsRendering = false;
            RenderProgress = null;
            InvalidateVisual();
        }

        /// <summary>Expands the card list into one slot per physical copy (Quantity).</summary>
        private List<ExpandedSlot> BuildExpandedSlots()
        {
            var slots = new List<ExpandedSlot>();
            var cards = CardsSource;
            if (cards != null)
                for (int i = 0; i < cards.Count; i++)
                    for (int q = 0; q < cards[i].Quantity; q++)
                        slots.Add(new ExpandedSlot(cards[i], i));
            return slots;
        }

        /// <summary>Returns the source image paths whose (bleed-processed) display image isn't decoded
        /// in the bitmap cache yet — i.e. the ones that still need loading before this redraw.</summary>
        private HashSet<string> CollectPathsToLoad(List<ExpandedSlot> slots)
        {
            var pathsToLoad = new HashSet<string>();
            foreach (var s in slots)
            {
                bool showBack = IsCardFlipped(s.CardIndex);
                string? path = showBack ? (s.Card.BackArtworkPath ?? s.Card.ArtworkPath) : s.Card.ArtworkPath;
                if (string.IsNullOrEmpty(path)) continue;
                if (_displayPathCache.TryGetValue(DisplayKey(path, _bleedMm, _processBleed, _cardWmm), out var disp)
                    && _imageCache.ContainsKey(disp))
                    continue;
                pathsToLoad.Add(path);
            }
            return pathsToLoad;
        }

        /// <summary>
        /// Resolves each path to its display image (Scryfall edge-extend / MPCFill crop, or raw in
        /// registration-marks mode) on a background thread and decodes it into the bitmap cache, yielding
        /// to the UI between images. Returns false if the redraw was cancelled mid-load.
        /// </summary>
        private async Task<bool> PreloadDisplayImagesAsync(HashSet<string> pathsToLoad, int decodeWidth, CancellationToken token)
        {
            IsRendering = true;
            int loaded = 0, total = pathsToLoad.Count;
            double bleedMm = _bleedMm;
            bool processBleed = _processBleed;
            double cardWmm = _cardWmm;
            foreach (var path in pathsToLoad)
            {
                if (token.IsCancellationRequested) return false;
                await Task.Run(() =>
                {
                    string disp = _displayPathCache.GetOrAdd(DisplayKey(path, bleedMm, processBleed, cardWmm),
                        _ => processBleed
                            ? (_bleedProcessor.GetDisplayImage(path, bleedMm, cardWmm) ?? path)
                            : path);
                    LoadImageToCache(disp, decodeWidth);
                }, token);
                loaded++;
                RenderProgress = $"Loading images ({loaded}/{total})...";
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            return true;
        }

        private bool IsCardFlipped(int cardIndex) => _allFlipped ^ _flippedCardIndices.Contains(cardIndex);

        // ================================================================
        //  RENDER
        // ================================================================

        private static readonly Typeface ChromeTypeface = new("Segoe UI");

        public override void Render(DrawingContext context)
        {
            base.Render(context); // paints the gray Background

            if (_cols <= 0 || _rows <= 0 || _perPage <= 0 || _expandedSlots == null) return;

            var slots = _expandedSlots;

            for (int page = 0; page < _totalPages; page++)
            {
                float pageTop = page * (_pageH + PageGapPx);

                if (page > 0)
                {
                    var lbl = ChromeText($"Page {page + 1} of {_totalPages}", 12, Brushes.Gray, FontWeight.SemiBold);
                    context.DrawText(lbl, new Point(4, pageTop - 18));
                }

                // Page background + margin box.
                context.DrawRectangle(PageFill, PagePen, new Rect(0, pageTop, _pageW, _pageH));
                context.DrawRectangle(null, MarginPen,
                    new Rect(_marginL, pageTop + _marginT, _pageW - _marginL - _marginR, _pageH - _marginT - _marginB));

                int pageStart = page * _perPage;

                // Pass 1: cut guides BEHIND the cards (matches the PDF) — only for occupied cells.
                if (ShowCutGuides && !_regMarksActive)
                {
                    for (int r = 0; r < _rows; r++)
                        for (int c = 0; c < _cols; c++)
                        {
                            int flat = pageStart + r * _cols + c;
                            if (flat >= slots.Count) continue;
                            float gx = _marginL + c * _cellW;
                            float gy = pageTop + _marginT + r * _cellH;
                            CardVisualRenderer.PaintCutGuides(context, gx, gy, _bleed, _cardW, _cardH, pageTop, _pageW, _pageH);
                        }
                }

                // Pass 2: slot backgrounds + card art.
                for (int r = 0; r < _rows; r++)
                {
                    for (int c = 0; c < _cols; c++)
                    {
                        int flat = pageStart + r * _cols + c;
                        float x = _marginL + c * _cellW;
                        float y = pageTop + _marginT + r * _cellH;

                        context.DrawRectangle(SlotFill, SlotPen, new Rect(x, y, _cellW, _cellH));

                        if (flat < slots.Count)
                        {
                            var es = slots[flat];
                            PaintCard(context, es.Card, x, y, IsCardFlipped(es.CardIndex));
                        }
                    }
                }

                // Page number (bottom-right).
                var pn = ChromeText($"{page + 1}", 14, PageNumBrush, FontWeight.Bold);
                context.DrawText(pn, new Point(_pageW - 30, pageTop + _pageH - 25));

                if (_regMarksActive && PrintSettingsSource is { } regPs)
                    DrawRegistrationMarksPreview(context, pageTop, _pageW, _pageH, regPs);
            }

            // Selection outlines.
            if (_cellW > 0 && _cellH > 0)
                foreach (int slot in _selectedSlots)
                {
                    if (slot < 0 || slot >= slots.Count) continue;
                    var (sx, sy) = SlotToPosition(slot);
                    context.DrawRectangle(null, SelectionPen, new Rect(sx, sy, _cellW, _cellH));
                }

            // Drop highlight + drag ghost (only while dragging).
            if (_isDragging)
            {
                if (_dropHighlightSlot >= 0)
                {
                    var (dx, dy) = SlotToPosition(_dropHighlightSlot);
                    context.DrawRectangle(DropFill, DropPen, new Rect(dx, dy, _cellW, _cellH), 3, 3);
                }

                var ghostRect = new Rect(_dragGhostCenter.X - _cellW / 2, _dragGhostCenter.Y - _cellH / 2, _cellW, _cellH);
                using (context.PushOpacity(0.7))
                {
                    if (_dragGhostBitmap != null)
                        context.DrawImage(_dragGhostBitmap, ghostRect);
                    else
                        context.DrawRectangle(GhostFill, GhostPen, ghostRect, 4, 4);
                }
            }
        }

        private static FormattedText ChromeText(string text, double size, IBrush brush, FontWeight weight)
            => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(ChromeTypeface.FontFamily, FontStyle.Normal, weight), size, brush);

        private const float InToPx = 96f;

        private void DrawRegistrationMarksPreview(DrawingContext ctx, float pageTop, float pageW, float pageH, PrintSettings ps)
        {
            float zoom   = (float)ZoomLevel;
            float inset  = ps.RegMarkInsetIn    * InToPx * zoom;
            float length = ps.RegMarkLengthIn   * InToPx * zoom;
            float thick  = ps.RegMarkThicknessIn* InToPx * zoom;

            void Mark(float rx, float ry, float rw, float rh)
                => ctx.DrawRectangle(Brushes.Black, null, new Rect(rx, ry, rw, rh));

            Mark(inset,                  pageTop + inset,             length, length);
            Mark(pageW - inset - length, pageTop + inset,             length, thick);
            Mark(pageW - inset - thick,  pageTop + inset + thick,     thick,  length - thick);
            Mark(inset,                  pageTop + pageH - inset - length, thick, length - thick);
            Mark(inset,                  pageTop + pageH - inset - thick,  length, thick);
        }

        private void PaintCard(DrawingContext ctx, CardModel card, float x, float y, bool flipped)
        {
            bool hasBackArt = !string.IsNullOrEmpty(card.BackArtworkPath);
            string? imagePath = flipped ? (hasBackArt ? card.BackArtworkPath : null) : card.ArtworkPath;

            Bitmap? bmp = null;
            bool bledImage = false;
            if (!(flipped && !hasBackArt) && !string.IsNullOrEmpty(imagePath))
            {
                string disp = _displayPathCache.TryGetValue(DisplayKey(imagePath, _bleedMm, _processBleed, _cardWmm), out var d)
                    ? d : imagePath;
                bmp = GetCachedImage(disp);
                bledImage = _useBleed && bmp != null
                    && (!string.Equals(disp, imagePath, StringComparison.Ordinal)
                        || BleedProcessor.ImageAlreadyHasBleed(imagePath));
            }

            CardVisualRenderer.PaintCard(ctx, card, bmp,
                x, y, _cellW, _cellH, _bleed, _cardW, _cardH,
                flipped, selected: false, PrintSettingsSource, bledImage);
        }

        // ================================================================
        //  IMAGE CACHE
        // ================================================================

        private static void LoadImageToCache(string path, int decodeWidth)
        {
            if (_imageCache.ContainsKey(path)) return;

            Bitmap? bmp = null;
            try
            {
                if (File.Exists(path))
                {
                    using var stream = File.OpenRead(path);
                    bmp = Bitmap.DecodeToWidth(stream, decodeWidth);
                }
            }
            catch { bmp = null; }

            // TryAdd so two threads decoding the same path don't both insert. If we lost the race the
            // bitmap we just decoded isn't on screen, so it's safe to dispose right away.
            if (!_imageCache.TryAdd(path, bmp))
            {
                bmp?.Dispose();
                return;
            }

            _imageCacheOrder.Enqueue(path);
            while (_imageCache.Count > MaxCachedImages && _imageCacheOrder.TryDequeue(out var oldest))
                _imageCache.TryRemove(oldest, out _);
        }

        private static Bitmap? GetCachedImage(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return _imageCache.TryGetValue(path, out var bmp) ? bmp : null;
        }

        // ================================================================
        //  POINTER EVENTS
        // ================================================================

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            // Focus is needed for keyboard shortcuts. The scroll-to-top that this used to cause is
            // suppressed deterministically by BringIntoViewOnFocusChange=false plus the
            // RequestBringIntoView handler set in the constructor — no offset save/restore needed.
            Focus();
            var pos = e.GetPosition(this);
            int flatSlot = HitTestSlot(pos);

            if (flatSlot >= 0 && flatSlot < _expandedSlots.Count)
            {
                int cardIdx = _expandedSlots[flatSlot].CardIndex;

                if (e.ClickCount == 2)
                {
                    CardDoubleClicked?.Invoke(_expandedSlots[flatSlot].Card, IsCardFlipped(cardIdx));
                    e.Handled = true;
                    return;
                }

                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    if (_selectedSlots.Contains(flatSlot)) _selectedSlots.Remove(flatSlot);
                    else _selectedSlots.Add(flatSlot);
                    _lastSelectedSlot = flatSlot;
                    SyncSelectedCard();
                    UpdateSelectionHighlight();
                    e.Handled = true;
                    return;
                }

                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _lastSelectedSlot >= 0)
                {
                    int from = Math.Min(_lastSelectedSlot, flatSlot);
                    int to   = Math.Max(_lastSelectedSlot, flatSlot);
                    for (int s = from; s <= to; s++)
                        if (s < _expandedSlots.Count) _selectedSlots.Add(s);
                    SyncSelectedCard();
                    UpdateSelectionHighlight();
                    e.Handled = true;
                    return;
                }

                _dragSourceCardIndex = cardIdx;
                _dragStart = pos;
                _isDragging = false;
                _pendingSelectSlot = flatSlot;
                _capturedPointer = e.Pointer;
                e.Pointer.Capture(this);
            }
            else
            {
                if (_selectedSlots.Count > 0)
                {
                    _selectedSlots.Clear();
                    _lastSelectedSlot = -1;
                    SyncSelectedCard();
                    UpdateSelectionHighlight();
                }
            }
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_dragSourceCardIndex < 0 || _capturedPointer == null) return;
            var pos   = e.GetPosition(this);
            var delta = pos - _dragStart;
            if (!_isDragging)
            {
                if (Math.Abs(delta.X) < 5 && Math.Abs(delta.Y) < 5) return;
                _isDragging = true;
                _dragGhostBitmap = CardsSource != null && _dragSourceCardIndex < CardsSource.Count
                    ? GetCachedImage(CardsSource[_dragSourceCardIndex].ArtworkPath)
                    : null;
            }
            _dragGhostCenter = pos;
            _dropHighlightSlot = HitTestSlot(pos);
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (e.InitialPressMouseButton != MouseButton.Left || _capturedPointer == null) return;

            _capturedPointer.Capture(null);
            _capturedPointer = null;

            if (_isDragging && _dragSourceCardIndex >= 0)
                PerformDrop(HitTestSlot(e.GetPosition(this)));
            else if (_pendingSelectSlot >= 0 && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                _selectedSlots.Clear();
                _selectedSlots.Add(_pendingSelectSlot);
                _lastSelectedSlot = _pendingSelectSlot;
                SyncSelectedCard();
                UpdateSelectionHighlight();
            }

            bool wasDragging = _isDragging;
            _dragGhostBitmap = null; _dropHighlightSlot = -1;
            _isDragging = false; _dragSourceCardIndex = -1; _pendingSelectSlot = -1;
            if (wasDragging) InvalidateVisual();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape && _selectedSlots.Count > 0)
            {
                _selectedSlots.Clear();
                SyncSelectedCard();
                UpdateSelectionHighlight();
                e.Handled = true;
            }
        }

        // ================================================================
        //  CONTEXT MENU — built dynamically on right-click
        // ================================================================

        private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            e.TryGetPosition(this, out var pos);
            int flatSlot    = HitTestSlot(pos);
            int hoverCardIdx = (flatSlot >= 0 && flatSlot < _expandedSlots.Count) ? _expandedSlots[flatSlot].CardIndex : -1;

            var menu         = new ContextMenu();
            bool hasSelection = _selectedSlots.Count > 0;
            bool hasHover     = hoverCardIdx >= 0;
            string target     = hasSelection ? $" ({_selectedSlots.Count} selected)" : "";

            if (hasHover || hasSelection)
            {
                var cardIndices = hasSelection ? SelectedCardIndices() : new List<int> { hoverCardIdx };

                void Add(string header, Action action)
                {
                    var item = new MenuItem { Header = header };
                    item.Click += (_, _) => action();
                    menu.Items.Add(item);
                }

                Add(hasSelection ? $"Duplicate Selected{target}" : "Duplicate Card", () => DuplicateCards(cardIndices));
                Add(hasSelection ? $"Delete Selected{target}"   : "Delete Card",     () => DeleteCards(cardIndices));
                menu.Items.Add(new Separator());
                Add(hasSelection ? $"Flip Selected{target}"     : "Flip Card",       () => FlipCards(cardIndices));
                Add(hasSelection ? $"Match Back Art{target}"    : "Match Back Art",  () => ApplyMajorityBackRequested?.Invoke(cardIndices));
                menu.Items.Add(new Separator());
                Add(hasSelection ? $"Select Front Art{target}"  : "Select Front Art...", () => SelectFrontArtRequested?.Invoke(cardIndices));
                Add(hasSelection ? $"Select Card Back{target}"  : "Select Card Back...", () => SelectBackArtRequested?.Invoke(cardIndices));
                menu.Items.Add(new Separator());

                if (hasSelection)
                    Add($"Create Token(s) from Selected{target}", () => CreateTokensFromCardsRequested?.Invoke(
                        cardIndices.Where(i => i >= 0 && i < CardsSource!.Count)
                                   .Select(i => CardsSource![i]).ToList()));
                else if (hasHover)
                    Add("Create Token Card", () => CreateTokenRequested?.Invoke(CardsSource![hoverCardIdx]));
            }

            var flipAll = new MenuItem { Header = _allFlipped ? "Unflip All Cards" : "Flip All Cards" };
            flipAll.Click += (_, _) => FlipAll();
            menu.Items.Add(flipAll);

            if (hasSelection)
            {
                menu.Items.Add(new Separator());
                var clear = new MenuItem { Header = "Clear Selection" };
                clear.Click += (_, _) => { _selectedSlots.Clear(); SyncSelectedCard(); UpdateSelectionHighlight(); };
                menu.Items.Add(clear);
            }

            ContextMenu = menu;
        }

        // ================================================================
        //  CARD OPERATIONS
        // ================================================================

        private List<int> SelectedCardIndices() =>
            _selectedSlots
                .Where(s => s >= 0 && s < _expandedSlots.Count)
                .Select(s => _expandedSlots[s].CardIndex)
                .Distinct().ToList();

        private void DuplicateCards(List<int> cardIndices)
        {
            if (CardsSource == null) return;
            CanvasOperations.DuplicateCards(CardsSource, cardIndices, UndoSvc);
            _selectedSlots.Clear(); SyncSelectedCard();
        }

        private void DeleteCards(List<int> cardIndices)
        {
            if (CardsSource == null) return;
            CanvasOperations.DeleteCards(CardsSource, cardIndices, UndoSvc);
            _selectedSlots.Clear(); SyncSelectedCard();
        }

        private void FlipCards(List<int> idx) { CanvasOperations.FlipCards(_flippedCardIndices, idx); RedrawSafe(); }
        private void FlipAll()                 { CanvasOperations.FlipAll(ref _allFlipped, _flippedCardIndices); RedrawSafe(); }

        // ================================================================
        //  DRAG AND DROP
        // ================================================================

        private void SyncSelectedCard()
        {
            if (_selectedSlots.Count == 0 || _expandedSlots.Count == 0) { SelectedCard = null; return; }
            // Show the last-clicked slot, not _selectedSlots.First() — a HashSet has no defined order,
            // so with a multi-selection that would surface an arbitrary card in the property panel.
            int slot = (_lastSelectedSlot >= 0 && _selectedSlots.Contains(_lastSelectedSlot))
                ? _lastSelectedSlot
                : _selectedSlots.First();
            SelectedCard = (slot >= 0 && slot < _expandedSlots.Count) ? _expandedSlots[slot].Card : null;
        }

        private void PerformDrop(int targetFlatSlot)
        {
            var cards = CardsSource;
            if (cards == null || _dragSourceCardIndex < 0) return;
            UndoSvc?.SaveState(cards);
            int targetCardIndex = (targetFlatSlot >= 0 && targetFlatSlot < _expandedSlots.Count)
                ? _expandedSlots[targetFlatSlot].CardIndex
                : cards.Count - 1;
            if (targetCardIndex == _dragSourceCardIndex) return;
            cards.Move(_dragSourceCardIndex, targetCardIndex);
        }

        // ================================================================
        //  HIT TESTING
        // ================================================================

        private int HitTestSlot(Point pos)
        {
            if (_perPage <= 0 || _cols <= 0 || _rows <= 0) return -1;
            float pageStride = _pageH + PageGapPx;
            int page = Math.Clamp((int)(pos.Y / pageStride), 0, _totalPages - 1);
            float pageTop = page * pageStride;
            float localY = (float)pos.Y - pageTop;
            float localX = (float)pos.X;
            if (localY < _marginT || localY >= _marginT + _rows * _cellH) return -1;
            if (localX < _marginL || localX >= _marginL + _cols * _cellW) return -1;
            int col = (int)((localX - _marginL) / _cellW);
            int row = (int)((localY - _marginT) / _cellH);
            if (col < 0 || col >= _cols || row < 0 || row >= _rows) return -1;
            return page * _perPage + row * _cols + col;
        }

        private (float x, float y) SlotToPosition(int flatSlot)
        {
            int page      = flatSlot / _perPage;
            int slotOnPage = flatSlot % _perPage;
            float pageTop = page * (_pageH + PageGapPx);
            return (_marginL + (slotOnPage % _cols) * _cellW, pageTop + _marginT + (slotOnPage / _cols) * _cellH);
        }

        // Selection changed: just repaint. With retained rendering the selection outlines are drawn from
        // _selectedSlots in Render, so there are no overlay controls to add/remove, and no full rebuild
        // (which would reload images and could jump the ScrollViewer).
        private void UpdateSelectionHighlight() => InvalidateVisual();
    }
}
