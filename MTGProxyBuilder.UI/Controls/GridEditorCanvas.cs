using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.Controls
{
    public class GridEditorCanvas : Canvas
    {
        private const float MmToPx = 96f / 25.4f;
        private const float PageGapPx = 30f;

        // Drag state
        private bool _isDragging;
        private int _dragSourceCardIndex = -1;
        private int _pendingSelectSlot = -1;
        private Point _dragStart;
        private Control? _dragGhost;
        private Rectangle? _dropHighlight;

        // Pointer capture
        private IPointer? _capturedPointer;

        // Selection state
        private readonly HashSet<int> _selectedSlots = new();
        private int _lastSelectedSlot = -1;

        // Flip state
        private readonly HashSet<int> _flippedCardIndices = new();
        private bool _allFlipped;

        // Cached layout info for hit testing
        private float _pageW, _pageH, _cellW, _cellH, _marginL, _marginT;
        private int _cols, _rows, _perPage, _totalPages;
        private List<ExpandedSlot> _expandedSlots = new();

        // Image cache
        private static readonly ConcurrentDictionary<string, Bitmap?> _imageCache = new();

        // Debounce + async redraw
        private DispatcherTimer? _redrawTimer;
        private CancellationTokenSource? _redrawCts;

        private record ExpandedSlot(CardModel Card, int CardIndex);

        public GridEditorCanvas()
        {
            Background = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
            ClipToBounds = true;
            Focusable = true;

            _redrawTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _redrawTimer.Tick += (_, _) => { _redrawTimer.Stop(); _ = RedrawAsync(); };

            ContextRequested += OnContextRequested;
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
        // Avalonia has no LayoutTransform; scaling the coordinate system is the portable workaround.
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
        //  ASYNC RENDERING
        // ================================================================

        private async Task RedrawAsync()
        {
            _redrawCts?.Cancel();
            _redrawCts = new CancellationTokenSource();
            var token = _redrawCts.Token;

            var settings = PageSettings;
            if (settings == null) { Children.Clear(); return; }

            // Apply zoom by scaling the pixel-per-mm conversion factor.
            float mmPx = MmToPx * (float)ZoomLevel;

            float pageW  = settings.PageWidthMm  * mmPx;
            float pageH  = settings.PageHeightMm * mmPx;
            float marginL = settings.MarginLeftMm  * mmPx;
            float marginT = settings.MarginTopMm   * mmPx;
            float marginR = settings.MarginRightMm * mmPx;
            float marginB = settings.MarginBottomMm* mmPx;
            float cellW   = (settings.CardWidthMm  + 2 * settings.BleedWidthMm) * mmPx;
            float cellH   = (settings.CardHeightMm + 2 * settings.BleedWidthMm) * mmPx;
            float bleed   = settings.BleedWidthMm  * mmPx;
            float cardW   = settings.CardWidthMm   * mmPx;
            float cardH   = settings.CardHeightMm  * mmPx;
            int cols = settings.CardsPerRow;
            int rows = settings.CardsPerColumn;
            int perPage  = settings.CardsPerPage;

            if (cols <= 0 || rows <= 0 || perPage <= 0) { Children.Clear(); return; }

            var slots = new List<ExpandedSlot>();
            var cards = CardsSource;
            if (cards != null)
                for (int i = 0; i < cards.Count; i++)
                    for (int q = 0; q < cards[i].Quantity; q++)
                        slots.Add(new ExpandedSlot(cards[i], i));

            var pathsToLoad = new HashSet<string>();
            foreach (var s in slots)
            {
                bool showBack = IsCardFlipped(s.CardIndex);
                string? path = showBack ? (s.Card.BackArtworkPath ?? s.Card.ArtworkPath) : s.Card.ArtworkPath;
                if (!string.IsNullOrEmpty(path) && !_imageCache.ContainsKey(path))
                    pathsToLoad.Add(path);
            }

            if (pathsToLoad.Count > 0)
            {
                IsRendering = true;
                int loaded = 0, total = pathsToLoad.Count;
                foreach (var path in pathsToLoad)
                {
                    if (token.IsCancellationRequested) { IsRendering = false; return; }
                    await Task.Run(() => LoadImageToCache(path, (int)cellW * 2), token);
                    loaded++;
                    RenderProgress = $"Loading images ({loaded}/{total})...";
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                }
            }

            if (token.IsCancellationRequested) { IsRendering = false; return; }

            _pageW = pageW; _pageH = pageH; _cellW = cellW; _cellH = cellH;
            _marginL = marginL; _marginT = marginT;
            _cols = cols; _rows = rows; _perPage = perPage;
            _expandedSlots = slots;

            int totalSlots = slots.Count;
            _totalPages = totalSlots > 0 ? (int)Math.Ceiling((double)totalSlots / perPage) : 1;
            if (_totalPages < 1) _totalPages = 1;

            Children.Clear();
            _isDragging = false; _dragGhost = null; _dropHighlight = null;

            Width  = pageW;
            Height = _totalPages * pageH + (_totalPages - 1) * PageGapPx;

            IsRendering = true;
            for (int page = 0; page < _totalPages; page++)
            {
                if (token.IsCancellationRequested) { IsRendering = false; return; }
                RenderProgress = _totalPages > 1
                    ? $"Rendering page {page + 1} of {_totalPages}..."
                    : "Rendering page...";
                float pageTop = page * (pageH + PageGapPx);

                if (page > 0)
                {
                    var lbl = new TextBlock
                    {
                        Text = $"Page {page + 1} of {_totalPages}",
                        FontSize = 12, Foreground = Brushes.Gray, FontWeight = FontWeight.SemiBold
                    };
                    SetLeft(lbl, 4); SetTop(lbl, pageTop - 18); Children.Add(lbl);
                }

                var bg = new Rectangle { Width = pageW, Height = pageH, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 0.5 };
                SetLeft(bg, 0); SetTop(bg, pageTop); Children.Add(bg);

                var mr = new Rectangle
                {
                    Width = pageW - marginL - marginR, Height = pageH - marginT - marginB,
                    Stroke = Brushes.LightBlue, StrokeThickness = 0.5,
                    StrokeDashArray = new AvaloniaList<double> { 4, 4 }
                };
                SetLeft(mr, marginL); SetTop(mr, pageTop + marginT); Children.Add(mr);

                int pageStart = page * perPage;
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        int flat = pageStart + r * cols + c;
                        float x = marginL + c * cellW;
                        float y = pageTop + marginT + r * cellH;

                        var slot = new Rectangle
                        {
                            Width = cellW, Height = cellH,
                            Fill = new SolidColorBrush(Color.FromArgb(15, 0, 0, 0)),
                            Stroke = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                            StrokeThickness = 0.5, StrokeDashArray = new AvaloniaList<double> { 2, 2 }
                        };
                        SetLeft(slot, x); SetTop(slot, y); Children.Add(slot);

                        if (flat < slots.Count)
                        {
                            var es = slots[flat];
                            bool flipped  = IsCardFlipped(es.CardIndex);
                            bool selected = _selectedSlots.Contains(flat);
                            PlaceCardVisual(es.Card, es.CardIndex, x, y, cellW, cellH, bleed, cardW, cardH, pageTop, pageW, pageH, flipped, selected);
                        }
                    }
                }

                var pn = new TextBlock
                {
                    Text = $"{page + 1}", FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
                    FontWeight = FontWeight.Bold
                };
                SetLeft(pn, pageW - 30); SetTop(pn, pageTop + pageH - 25); Children.Add(pn);

                var regPs = PrintSettingsSource;
                if (regPs?.ShowRegistrationMarks == true)
                    DrawRegistrationMarksPreview(pageTop, pageW, pageH, regPs);

                if (_totalPages > 1)
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            }

            IsRendering = false;
            RenderProgress = null;
        }

        private const float InToPx = 96f;

        private void DrawRegistrationMarksPreview(float pageTop, float pageW, float pageH, PrintSettings ps)
        {
            float zoom   = (float)ZoomLevel;
            float inset  = ps.RegMarkInsetIn    * InToPx * zoom;
            float length = ps.RegMarkLengthIn   * InToPx * zoom;
            float thick  = ps.RegMarkThicknessIn* InToPx * zoom;

            void AddMark(float rx, float ry, float rw, float rh)
            {
                var r = new Rectangle { Width = rw, Height = rh, Fill = Brushes.Black, IsHitTestVisible = false };
                SetLeft(r, rx); SetTop(r, ry); Children.Add(r);
            }

            AddMark(inset,                  pageTop + inset,             length, length);
            AddMark(pageW - inset - length, pageTop + inset,             length, thick);
            AddMark(pageW - inset - thick,  pageTop + inset + thick,     thick,  length - thick);
            AddMark(inset,                  pageTop + pageH - inset - length, thick, length - thick);
            AddMark(inset,                  pageTop + pageH - inset - thick,  length, thick);
        }

        private bool IsCardFlipped(int cardIndex) => _allFlipped ^ _flippedCardIndices.Contains(cardIndex);

        // ================================================================
        //  IMAGE CACHE
        // ================================================================

        private static void LoadImageToCache(string path, int decodeWidth)
        {
            if (_imageCache.ContainsKey(path)) return;
            try
            {
                if (!File.Exists(path)) { _imageCache[path] = null; return; }
                using var stream = File.OpenRead(path);
                _imageCache[path] = Bitmap.DecodeToWidth(stream, decodeWidth);
            }
            catch { _imageCache[path] = null; }
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
                    _ = RedrawAsync();
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
                    _ = RedrawAsync();
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
                    _ = RedrawAsync();
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
                CreateDragGhost();
            }
            if (_dragGhost != null) { SetLeft(_dragGhost, pos.X - _cellW / 2); SetTop(_dragGhost, pos.Y - _cellH / 2); }
            UpdateDropHighlight(pos);
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
                _ = RedrawAsync();
            }

            if (_dragGhost    != null) Children.Remove(_dragGhost);
            if (_dropHighlight != null) Children.Remove(_dropHighlight);
            _dragGhost = null; _dropHighlight = null;
            _isDragging = false; _dragSourceCardIndex = -1; _pendingSelectSlot = -1;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape && _selectedSlots.Count > 0)
            {
                _selectedSlots.Clear();
                SyncSelectedCard();
                _ = RedrawAsync();
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
                clear.Click += (_, _) => { _selectedSlots.Clear(); SyncSelectedCard(); _ = RedrawAsync(); };
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

        private void FlipButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int cardIndex)
            {
                FlipCards(new List<int> { cardIndex });
                e.Handled = true;
            }
        }

        private void FlipCards(List<int> cardIndices)
        {
            CanvasOperations.FlipCards(_flippedCardIndices, cardIndices);
            _ = RedrawAsync();
        }

        private void FlipAll()
        {
            CanvasOperations.FlipAll(ref _allFlipped, _flippedCardIndices);
            _ = RedrawAsync();
        }

        // ================================================================
        //  DRAG AND DROP
        // ================================================================

        private void CreateDragGhost()
        {
            if (CardsSource == null || _dragSourceCardIndex < 0) return;
            var card = CardsSource[_dragSourceCardIndex];
            var ghost = new Border
            {
                Width = _cellW, Height = _cellH, Opacity = 0.7,
                Background = new SolidColorBrush(Color.FromArgb(180, 70, 130, 200)),
                BorderBrush = Brushes.DodgerBlue, BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(4), IsHitTestVisible = false
            };
            var bmp = GetCachedImage(card.ArtworkPath);
            if (bmp != null) ghost.Background = new ImageBrush { Source = bmp, Stretch = Stretch.Fill };
            _dragGhost = ghost; Children.Add(_dragGhost);
        }

        private void SyncSelectedCard()
        {
            if (_selectedSlots.Count == 0 || _expandedSlots.Count == 0) { SelectedCard = null; return; }
            int slot = _selectedSlots.First();
            SelectedCard = (slot >= 0 && slot < _expandedSlots.Count) ? _expandedSlots[slot].Card : null;
        }

        private void UpdateDropHighlight(Point pos)
        {
            if (_dropHighlight != null) Children.Remove(_dropHighlight);
            int slot = HitTestSlot(pos);
            if (slot < 0) return;
            var (x, y) = SlotToPosition(slot);
            _dropHighlight = new Rectangle
            {
                Width = _cellW, Height = _cellH,
                Fill = new SolidColorBrush(Color.FromArgb(50, 0, 120, 255)),
                Stroke = Brushes.DodgerBlue, StrokeThickness = 2,
                RadiusX = 3, RadiusY = 3, IsHitTestVisible = false
            };
            SetLeft(_dropHighlight, x); SetTop(_dropHighlight, y); Children.Add(_dropHighlight);
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

        // ================================================================
        //  CARD VISUAL
        // ================================================================

        private void PlaceCardVisual(CardModel card, int cardIndex, float x, float y,
            float cellW, float cellH, float bleed, float cardW, float cardH,
            float pageTop, float pageW, float pageH, bool flipped, bool selected)
        {
            bool hasBackArt = !string.IsNullOrEmpty(card.BackArtworkPath);
            string? imagePath = flipped ? (hasBackArt ? card.BackArtworkPath : null) : card.ArtworkPath;
            var bmp = (flipped && !hasBackArt) ? null : GetCachedImage(imagePath);

            CardVisualRenderer.PlaceCard(this, card, bmp,
                x, y, cellW, cellH, bleed, cardW, cardH,
                pageTop, pageW, pageH, flipped, selected,
                ShowCutGuides, PrintSettingsSource);

            // Flip button overlay
            float btnSize = Math.Max(27, cardW * 0.135f);
            float btnMargin = 4f;
            var flipBtn = new Button
            {
                Width = btnSize, Height = btnSize,
                Content = "\u21BB", // ↻
                FontSize = btnSize * 0.6,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(200, 80, 80, 80)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = flipped ? "Show Front" : "Show Back",
                Tag = cardIndex
            };
            flipBtn.Click += FlipButton_Click;
            SetLeft(flipBtn, x + bleed + cardW - btnSize - btnMargin);
            SetTop(flipBtn, y + bleed + btnMargin);
            Children.Add(flipBtn);
        }
    }
}
