using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.Controls;
using MTGProxyBuilder.UI.Services;

namespace MTGProxyBuilder.UI.Dialogs;

public partial class ArtSelectorWindow : Window
{
    private readonly CardModel _card;
    private readonly ArtSelectorMode _mode;
    private readonly ScryfallService _scryfall;
    private readonly MpcFillService _mpcFill;
    private readonly YgoProDeckService? _ygo; // fork-specific: Yu-Gi-Oh! alternate artworks
    private readonly ImageCacheService _imageCache;
    private readonly BackArtLibraryService? _backLibrary;
    private readonly IReadOnlyList<CardModel>? _allCards;
    private readonly object[][]? _mpcSourcesOverride;
    private readonly FrontArtLibraryService? _frontArtLibrary;
    private readonly ThumbnailService? _frontThumbnails;
    private readonly ThumbnailService? _backThumbnails;
    private MpcFillSearchOptions _mpcSearchOptions;

    public string? ResultPath { get; private set; }
    public bool ApplyToSameName { get; private set; }
    public bool ApplyToNoBack { get; private set; }
    public bool ApplyToThisCopyOnly { get; private set; }

    private readonly Dictionary<string, ScryfallCard> _scryfallCardsByPath = new(StringComparer.OrdinalIgnoreCase);

    // Session cache of the online search results per card name, so reopening the selector for the same
    // (or an identically-named) card doesn't re-query Scryfall/MPCFill over the network. The "Research
    // MPCFill" button clears the entry to force a fresh search with the new filters.
    private static readonly ConcurrentDictionary<string, (List<ScryfallCard> Scryfall, List<MpcFillCard> Mpc)>
        _apiResultsCache = new(StringComparer.OrdinalIgnoreCase);

    private record TileInfo(Border Tile, string Name, string Source, string Detail, bool IsAction = false);
    private readonly List<TileInfo> _allTiles = new();

    private bool _filterPanelVisible;

    public ArtSelectorWindow(
        CardModel card,
        ArtSelectorMode mode,
        ScryfallService scryfall,
        MpcFillService mpcFill,
        ImageCacheService imageCache,
        BackArtLibraryService? backLibrary = null,
        IReadOnlyList<CardModel>? allCards = null,
        object[][]? mpcSourcesOverride = null,
        MpcFillSearchOptions? mpcSearchOptions = null,
        FrontArtLibraryService? frontArtLibrary = null,
        YgoProDeckService? ygo = null)
    {
        InitializeComponent();
        _card = card;
        _mode = mode;
        _scryfall = scryfall;
        _mpcFill = mpcFill;
        _ygo = ygo;
        _imageCache = imageCache;
        _backLibrary = backLibrary;
        _allCards = allCards;
        _mpcSourcesOverride = mpcSourcesOverride;
        _mpcSearchOptions = mpcSearchOptions ?? new MpcFillSearchOptions();
        _frontArtLibrary = frontArtLibrary;
        _frontThumbnails = frontArtLibrary != null ? new ThumbnailService(frontArtLibrary.LibraryDirectory) : null;
        _backThumbnails = backLibrary != null ? new ThumbnailService(backLibrary.LibraryDirectory) : null;

        bool isFront = mode == ArtSelectorMode.Front;
        TitleLabel.Text = isFront ? "Select Front Artwork" : "Select Card Back";
        CardNameLabel.Text = $"for: {card.Name}";

        if (isFront && _allCards != null)
        {
            int sameCount = _allCards.Count(c => c.Name == card.Name);
            if (sameCount > 1)
            {
                ApplySameNameChk.Content = $"Apply to all \"{card.Name}\" ({sameCount} cards)";
                ApplySameNameChk.IsVisible = true;
            }
        }

        if (!isFront && _allCards != null)
        {
            int noBackCount = _allCards.Count(c => string.IsNullOrEmpty(c.BackArtworkPath));
            if (noBackCount > 0)
            {
                ApplyNoBackChk.Content = $"Apply to all without back art ({noBackCount} cards)";
                ApplyNoBackChk.IsVisible = true;
            }
        }

        // Stacked card (Quantity > 1): offer to apply the chosen art to just ONE copy (splits it off),
        // instead of the whole stack. Mutually understood with "apply to all same name".
        if (card.Quantity > 1)
        {
            ApplyThisCopyChk.Content = $"This copy only (of {card.Quantity})";
            ApplyThisCopyChk.IsVisible = true;
        }

        if (isFront && _frontArtLibrary != null)
            ActionsBar.IsVisible = true;

        LoadFilterControls(_mpcSearchOptions);

        Loaded += async (_, _) => await LoadOptionsAsync();
    }

    // ================================================================
    //  LOAD OPTIONS
    // ================================================================

    private async Task LoadOptionsAsync()
    {
        OptionsPanel.Children.Clear();
        _allTiles.Clear();
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool isFront = _mode == ArtSelectorMode.Front;

        SpinnerDot.IsVisible = true;
        OkBtn.IsEnabled = false;
        ResultPath = null;

        string? currentPath = isFront ? _card.ArtworkPath : _card.BackArtworkPath;
        if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
        {
            AddOption("Current", currentPath, true, "Currently assigned");
            shown.Add(currentPath);
        }

        if (isFront)
            await LoadFrontOptionsAsync(shown);
        else
            await LoadBackOptionsAsync(shown);

        StatusLabel.Text = $"{shown.Count} option(s) found";
        SpinnerDot.IsVisible = false;
        PopulateSourceFilter();
        ApplyFilters();
    }

    private async Task LoadFrontOptionsAsync(HashSet<string> shown)
    {
        if (string.IsNullOrEmpty(_card.Name))
        {
            AddActionTile("Browse File...", () => _ = OnBrowseFileAsync());
            return;
        }

        var libraryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Library matches (★ green-bordered tiles, loaded before the online search)
        await AddLibraryMatchesAsync(shown, libraryNames);

        // Yu-Gi-Oh! cards live on YGOPRODeck, not Scryfall/MPCFill — show their alternate artworks
        // (the API returns several card_images per card) instead of the Magic sources.
        if (_ygo != null && _card.Source == CardSource.YgoProDeck)
        {
            await AddYgoArtworksAsync(shown);
            if (_frontArtLibrary == null)
                AddActionTile("Browse File...", () => _ = OnBrowseFileAsync());
            return;
        }

        // 2. API search
        StatusLabel.Text = $"Searching for \"{_card.Name}\"...";
        var mpcOpts = BuildSearchOptionsFromControls();
        mpcOpts.FuzzySearch = false;

        // Reuse this session's results for the same card name — no network round-trip on reopen.
        // (Scryfall's printing list is also disk-cached across restarts; MPCFill is re-queried once
        // per restart.) "Research MPCFill" clears this entry to force a fresh search.
        bool resultsWereCached = _apiResultsCache.ContainsKey(_card.Name);
        bool printingsWereCached = resultsWereCached || _scryfall.HasCachedPrintings(_card.Name);

        List<ScryfallCard> scryfallResults;
        List<MpcFillCard> mpcResults;
        if (_apiResultsCache.TryGetValue(_card.Name, out var cachedApi))
        {
            scryfallResults = cachedApi.Scryfall;
            mpcResults = cachedApi.Mpc;
        }
        else
        {
            var scryfallTask = Task.Run(async () =>
            {
                // every printing/version of this exact card (disk-cached across opens)
                try { return await _scryfall.GetAllPrintingsAsync(_card.Name); }
                catch { return new List<ScryfallCard>(); }
            });
            var mpcTask = Task.Run(async () =>
            {
                try
                {
                    var (results, _) = await _mpcFill.SearchAsync(
                        _card.Name, fuzzySearch: false, sourcesOverride: _mpcSourcesOverride, options: mpcOpts);
                    return results.Where(mc => mc.Name.Contains(_card.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                }
                catch { return new List<MpcFillCard>(); }
            });

            await Task.WhenAll(scryfallTask, mpcTask);
            scryfallResults = scryfallTask.Result;
            mpcResults = mpcTask.Result;
            _apiResultsCache[_card.Name] = (scryfallResults, mpcResults);
        }

        if (libraryNames.Count > 0)
            mpcResults = mpcResults.Where(mc => !libraryNames.Contains($"{mc.Name} [{mc.Source}]")).ToList();

        int totalImages = scryfallResults.Count + mpcResults.Count;

        // Only prompt about a large download the FIRST time (live search). On reopen the printing
        // list — and its images — are already cached, so don't nag the user again.
        if (totalImages > 200 && !printingsWereCached)
        {
            var confirmed = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var svc = new AvaloniaDialogService();
                return await svc.ConfirmAsync(
                    $"Found {totalImages} art options ({scryfallResults.Count} Scryfall, {mpcResults.Count} MPCFill).\n\n" +
                    "Downloading and caching all of these may take a while. Continue?",
                    "Large Number of Results");
            });

            if (!confirmed)
            {
                StatusLabel.Text = $"{totalImages} results found (download skipped)";
                AddActionTile("Browse File...", () => _ = OnBrowseFileAsync());
                return;
            }
        }

        int completed = 0;
        var semaphore = new SemaphoreSlim(8);

        // Add each tile AS its image resolves (not after the whole batch), and resolve already-cached
        // art instantly — skipping the semaphore and the download path entirely. So reopening the
        // selector shows every previously-downloaded option immediately, and on a first open the tiles
        // stream in instead of all appearing at once after the slowest download.
        void AddResultTile(string label, string path, string detail, ScryfallCard? card, string? source)
        {
            if (shown.Add(path))
            {
                AddOption(label, path, false, detail, source);
                if (card != null)
                    _scryfallCardsByPath[path] = card;
            }
        }

        async Task ResolveAsync(string? cachedPath, Func<Task<string?>> download,
            string label, string detail, ScryfallCard? card, string? source)
        {
            string? path = cachedPath;
            if (path == null)
            {
                await semaphore.WaitAsync();
                try { path = await download(); }
                finally { semaphore.Release(); }
            }
            var done = Interlocked.Increment(ref completed);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (path != null) AddResultTile(label, path, detail, card, source);
                StatusLabel.Text = $"Loading art {done}/{totalImages}...";
            });
        }

        var scryfallDownloads = scryfallResults
            .Where(sc => sc.GetImageUrl() != null)
            .Select(sc => ResolveAsync(
                _scryfall.GetCachedImagePath(sc, size: "normal"),
                () => _scryfall.DownloadAndCacheImageAsync(sc, size: "normal"),
                $"{sc.SetName} #{sc.CollectorNumber}", $"Scryfall | {sc.Artist ?? ""}", sc, null));

        var mpcDownloads = mpcResults.Select(mc => ResolveAsync(
            _mpcFill.GetCachedImagePath(mc),
            () => _mpcFill.DownloadAndCacheImageAsync(mc),
            mc.Name, $"MPCFill | {mc.Source} | {mc.Dpi} DPI", null, mc.Source));

        await Task.WhenAll(scryfallDownloads.Concat(mpcDownloads));

        if (_frontArtLibrary == null)
            AddActionTile("Browse File...", () => _ = OnBrowseFileAsync());
    }

    /// <summary>
    /// Adds a tile for each front-library entry matching the card name (★, green border) and loads
    /// their thumbnails. Records the matched names in <paramref name="libraryNames"/> so the online
    /// results can skip art that's already in the library.
    /// </summary>
    private async Task AddLibraryMatchesAsync(HashSet<string> shown, HashSet<string> libraryNames)
    {
        if (_frontArtLibrary == null) return;
        var libraryMatches = _frontArtLibrary.SearchByCardName(_card.Name);
        if (libraryMatches.Count == 0) return;

        StatusLabel.Text = $"Found {libraryMatches.Count} in library, searching online...";
        foreach (var m in libraryMatches) libraryNames.Add(m.Name);

        var deferredImages = new List<(Image img, string entryId, string path)>();
        foreach (var entry in libraryMatches)
            if (shown.Add(entry.FilePath))
                deferredImages.Add(BuildLibraryTile(entry));

        await LoadDeferredThumbnailsAsync(deferredImages, _frontThumbnails);
    }

    /// <summary>
    /// Adds a tile for each alternate artwork of this Yu-Gi-Oh! card. YGOPRODeck returns several
    /// <c>card_images</c> per card; each is downloaded (or read from cache) and shown as an option.
    /// </summary>
    private async Task AddYgoArtworksAsync(HashSet<string> shown)
    {
        StatusLabel.Text = $"Searching YGOPRODeck for \"{_card.Name}\"...";
        var (cards, _) = await _ygo!.SearchCardAsync(_card.Name, fuzzy: false);
        var card = cards.FirstOrDefault(c => c.Name.Equals(_card.Name, StringComparison.OrdinalIgnoreCase))
                   ?? cards.FirstOrDefault();
        if (card?.CardImages == null || card.CardImages.Count == 0) return;

        StatusLabel.Text = $"Loading {card.CardImages.Count} artwork(s)...";
        for (int i = 0; i < card.CardImages.Count; i++)
        {
            string? path = _ygo.GetCachedImagePath(card, i)
                           ?? await _ygo.DownloadAndCacheImageAsync(card, i);
            if (path != null && shown.Add(path))
                AddOption($"Artwork {i + 1}", path, false, $"YGOPRODeck | {card.Name}", "YGOPRODeck");
        }
    }

    /// <summary>Builds one green-bordered library tile (image + name + source) wired for select /
    /// double-click / context menu, adds it to the panel, and returns its deferred-image handle.</summary>
    private (Image img, string entryId, string path) BuildLibraryTile(BackArtEntry entry)
    {
        var border = new Border
        {
            Width = ArtTileBuilder.TileWidth, Height = ArtTileBuilder.TileHeight,
            Margin = new Thickness(4), Background = AppBrushes.TileBg,
            CornerRadius = new CornerRadius(4), Cursor = new Cursor(StandardCursorType.Hand),
            BorderThickness = new Thickness(2), BorderBrush = AppBrushes.AccentGreen,
            ClipToBounds = true
        };
        ToolTip.SetTip(border, $"{entry.Name}\nLibrary | {entry.Source}");

        var stack = new StackPanel();
        var imgBorder = new Border
        {
            Height = ArtTileBuilder.ImageHeight, Background = Brushes.Black,
            CornerRadius = new CornerRadius(3, 3, 0, 0), ClipToBounds = true
        };
        var img = new Image { Stretch = Stretch.UniformToFill };
        imgBorder.Child = img;
        stack.Children.Add(imgBorder);
        stack.Children.Add(new TextBlock
        {
            Text = "★ " + entry.Name, Foreground = AppBrushes.AccentGreen,
            FontSize = ArtTileBuilder.LabelFontSize, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(3, 4, 3, 0)
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"Library | {entry.Source}", Foreground = AppBrushes.TextMuted,
            FontSize = ArtTileBuilder.DetailFontSize, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(3, 0, 3, 2)
        });
        border.Child = stack;

        string path = entry.FilePath;
        string capturedName = entry.Name;
        string detail = $"Library | {entry.Source}";
        border.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) SelectOption(capturedName, path, detail, border); };
        border.DoubleTapped += (_, _) => { SelectOption(capturedName, path, detail, border); Close(); };
        border.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Right) ShowContextMenu(border, path, capturedName, null); };

        OptionsPanel.Children.Add(border);
        _allTiles.Add(new TileInfo(border, entry.Name, entry.Source, detail));
        return (img, entry.Id, entry.FilePath);
    }

    /// <summary>Loads tile thumbnails in batches of 20 on a background thread (via the shared
    /// thumbnail cache), assigning each to its Image as the batch completes.</summary>
    private static async Task LoadDeferredThumbnailsAsync(
        List<(Image img, string entryId, string path)> deferred, ThumbnailService? thumbSvc)
    {
        const int batchSize = 20;
        for (int i = 0; i < deferred.Count; i += batchSize)
        {
            var batch = deferred.Skip(i).Take(batchSize).ToList();
            var bitmaps = await Task.Run(() =>
            {
                var results = new List<Bitmap?>();
                foreach (var (_, entryId, p) in batch)
                {
                    var loadPath = thumbSvc?.GetOrCreate(entryId, p) ?? p;
                    results.Add(ArtTileBuilder.GetThumbnail(loadPath));
                }
                return results;
            });
            for (int j = 0; j < batch.Count && j < bitmaps.Count; j++)
                if (bitmaps[j] != null) batch[j].img.Source = bitmaps[j];
        }
    }

    private async Task LoadBackOptionsAsync(HashSet<string> shown)
    {
        if (!string.IsNullOrEmpty(_card.OriginalBackArtworkPath)
            && File.Exists(_card.OriginalBackArtworkPath)
            && !shown.Contains(_card.OriginalBackArtworkPath))
        {
            AddOption("Original (Scryfall)", _card.OriginalBackArtworkPath, false, "From Scryfall import");
            shown.Add(_card.OriginalBackArtworkPath);
        }

        var deferredImages = new List<(Image img, string entryId, string path)>();

        if (_backLibrary != null)
        {
            var entries = _backLibrary.Entries.Where(e => File.Exists(e.FilePath) && !shown.Contains(e.FilePath)).ToList();
            StatusLabel.Text = $"Loading {entries.Count} library entries...";

            foreach (var entry in entries)
            {
                shown.Add(entry.FilePath);
                bool isDefault = _backLibrary.IsDefault(entry.Id);

                var border = new Border
                {
                    Width = ArtTileBuilder.TileWidth, Height = ArtTileBuilder.TileHeight,
                    Margin = new Thickness(4), Background = AppBrushes.TileBg,
                    CornerRadius = new CornerRadius(4), Cursor = new Cursor(StandardCursorType.Hand),
                    BorderThickness = new Thickness(2),
                    BorderBrush = isDefault ? AppBrushes.AccentGreen : Brushes.Transparent,
                    ClipToBounds = true
                };
                ToolTip.SetTip(border, $"{entry.Name}\n{(isDefault ? "DEFAULT\n" : "")}From library");

                var stack = new StackPanel();
                var imgBorder = new Border
                {
                    Height = ArtTileBuilder.ImageHeight, Background = Brushes.Black,
                    CornerRadius = new CornerRadius(3, 3, 0, 0), ClipToBounds = true
                };
                var img = new Image { Stretch = Stretch.UniformToFill };
                imgBorder.Child = img;
                deferredImages.Add((img, entry.Id, entry.FilePath));
                stack.Children.Add(imgBorder);
                stack.Children.Add(new TextBlock
                {
                    Text = (isDefault ? "★ " : "") + entry.Name,
                    Foreground = isDefault ? AppBrushes.AccentGreen : AppBrushes.TextSecondary,
                    FontSize = ArtTileBuilder.LabelFontSize, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(3, 4, 3, 0)
                });
                stack.Children.Add(new TextBlock
                {
                    Text = "From library", Foreground = AppBrushes.TextMuted,
                    FontSize = ArtTileBuilder.DetailFontSize, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Margin = new Thickness(3, 0, 3, 2)
                });
                border.Child = stack;

                string path = entry.FilePath;
                string capturedName = entry.Name;
                border.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) SelectOption(capturedName, path, "From library", border); };
                border.DoubleTapped += (_, _) => { SelectOption(capturedName, path, "From library", border); Close(); };
                border.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Right) ShowContextMenu(border, path, capturedName, null); };

                OptionsPanel.Children.Add(border);
                _allTiles.Add(new TileInfo(border, entry.Name, entry.Source, "From library"));
            }
        }

        AddActionTile("Download MPCFill\nCard Backs", () => _ = OnDownloadMpcFillBacksAsync());
        if (_backLibrary != null) AddActionTile("+ Add to Library", () => _ = OnAddToLibraryAsync());
        AddActionTile("Browse File...", () => _ = OnBrowseFileAsync());

        StatusLabel.Text = $"{shown.Count} option(s) found";

        // Load thumbnails (shared loader, same batching as the front library)
        if (deferredImages.Count > 0)
        {
            await LoadDeferredThumbnailsAsync(deferredImages, _backThumbnails);
            StatusLabel.Text = $"{shown.Count} option(s) found";
        }
    }

    private async Task OnDownloadMpcFillBacksAsync()
    {
        if (_backLibrary == null) return;

        StatusLabel.Text = "Fetching card back list from MPCFill...";
        SpinnerDot.IsVisible = true;

        try
        {
            var (cardbacks, error) = await _mpcFill.SearchCardbacksAsync(500);
            if (error != null || cardbacks.Count == 0)
            {
                StatusLabel.Text = error ?? "No card backs found on MPCFill.";
                return;
            }

            StatusLabel.Text = $"Downloading {cardbacks.Count} card backs...";
            var results = await _mpcFill.DownloadAndCacheImagesAsync(cardbacks, maxConcurrency: 8,
                onProgress: (done, total, name) =>
                    Dispatcher.UIThread.InvokeAsync(() => StatusLabel.Text = $"Downloading card back {done}/{total}: {name}..."));

            int added = 0, skipped = 0;
            _backLibrary.BeginBatch();
            try
            {
                foreach (var (cb, cached) in results)
                {
                    if (cached == null) { skipped++; continue; }
                    string displayName = $"{cb.Name} [{cb.Source}]";
                    var entry = _backLibrary.AddFromFile(cached, displayName, cb.Source);
                    if (entry != null) { _backLibrary.ApplyMpcFillDefaults(entry.Id, cb.Source); added++; }
                    else skipped++;
                }
            }
            finally { _backLibrary.EndBatch(); }

            StatusLabel.Text = $"Added {added} card back(s) to library ({skipped} already existed or failed)";
            await LoadOptionsAsync();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
        finally { SpinnerDot.IsVisible = false; }
    }

    // ================================================================
    //  TILE BUILDERS
    // ================================================================

    private void AddOption(string label, string imagePath, bool isCurrent, string detail, string? mpcSource = null)
    {
        var border = ArtTileBuilder.CreateOptionTile(label, imagePath, isCurrent, detail);

        string path = imagePath;
        string capturedLabel = label;
        string capturedDetail = detail;
        border.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
                SelectOption(capturedLabel, path, capturedDetail, border);
            else if (e.InitialPressMouseButton == MouseButton.Right)
                ShowContextMenu(border, path, capturedLabel, mpcSource);
        };
        border.DoubleTapped += (_, _) => { SelectOption(capturedLabel, path, capturedDetail, border); Close(); };

        OptionsPanel.Children.Add(border);

        string trackSource = !string.IsNullOrEmpty(mpcSource) ? mpcSource
            : capturedDetail.Contains("Scryfall", StringComparison.OrdinalIgnoreCase) ? "Scryfall"
            : capturedDetail.Contains("Library", StringComparison.OrdinalIgnoreCase) ? "Library"
            : "";
        _allTiles.Add(new TileInfo(border, label, trackSource, detail));
    }

    private void AddActionTile(string label, Action action)
    {
        var border = ArtTileBuilder.CreateActionTile(label);
        border.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) action(); };
        OptionsPanel.Children.Add(border);
        _allTiles.Add(new TileInfo(border, label, "", "", IsAction: true));
    }

    private void ShowContextMenu(Border tile, string path, string label, string? mpcSource)
    {
        var menu = new ContextMenu();

        var previewItem = new MenuItem { Header = "Preview Full Size" };
        previewItem.Click += (_, _) =>
        {
            var preview = new ImagePreviewWindow(path, label);
            _ = preview.ShowDialog(this);
        };
        menu.Items.Add(previewItem);

        if (_frontArtLibrary != null && mpcSource != null)
        {
            var saveItem = new MenuItem { Header = "Save to Library" };
            saveItem.Click += (_, _) =>
            {
                string libName = $"{label} [{mpcSource}]";
                var entry = _frontArtLibrary.AddFromFile(path, libName, mpcSource);
                if (entry != null)
                {
                    if (_scryfallCardsByPath.TryGetValue(path, out var sc))
                        _frontArtLibrary.ApplyMetadata(entry.Id, sc);
                    _frontArtLibrary.ApplyMpcFillDefaults(entry.Id, mpcSource);
                }
                StatusLabel.Text = entry != null
                    ? $"Saved \"{libName}\" to front art library"
                    : $"\"{libName}\" already in library";
            };
            menu.Items.Add(saveItem);
        }

        menu.Open(tile);
    }

    // ================================================================
    //  SELECTION
    // ================================================================

    private void SelectOption(string label, string path, string detail, Border selectedBorder)
    {
        foreach (var child in OptionsPanel.Children)
            if (child is Border b) b.BorderBrush = Brushes.Transparent;
        selectedBorder.BorderBrush = Brushes.DodgerBlue;

        ResultPath = path;
        OkBtn.IsEnabled = true;
        PreviewPanel.ShowImage(path, label, detail);
    }

    // ================================================================
    //  FILE BROWSE ACTIONS
    // ================================================================

    private async Task OnBrowseFileAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;

        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select Artwork",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Image Files") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp" } },
                new Avalonia.Platform.Storage.FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count == 0) return;
        var path = files[0].Path.LocalPath;
        if (string.IsNullOrEmpty(path)) return;

        ResultPath = path;
        OkBtn.IsEnabled = true;
        PreviewPanel.ShowImage(path, Path.GetFileName(path), "Local file");
    }

    private async Task OnAddToLibraryAsync()
    {
        if (_backLibrary == null) return;

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;

        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Add Image to Back Art Library",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Image Files") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" } }
            }
        });

        if (files.Count == 0) return;
        var path = files[0].Path.LocalPath;
        if (path != null) _backLibrary.AddFromFile(path);
        await LoadOptionsAsync();
    }

    private async void OnImportCacheToLibraryClick(object? sender, RoutedEventArgs e)
    {
        if (_frontArtLibrary == null) return;

        var cached = _imageCache.GetCachedByPrefix("mpc_");
        if (cached.Count == 0) { StatusLabel.Text = "No downloaded MPCFill art found in cache."; return; }

        var scryfallCard = _scryfallCardsByPath.Values.FirstOrDefault();

        int added = 0, skipped = 0;
        var newEntries = new List<(string Id, string FilePath)>();
        var importedCacheKeys = new List<string>();
        _frontArtLibrary.BeginBatch();
        try
        {
            foreach (var (key, path, name, source) in cached)
            {
                if (!File.Exists(path)) { skipped++; continue; }
                string displayName = !string.IsNullOrEmpty(source) ? $"{name} [{source}]" : name;
                var entry = _frontArtLibrary.AddFromFile(path, displayName, source);
                if (entry != null)
                {
                    added++;
                    newEntries.Add((entry.Id, entry.FilePath));
                    importedCacheKeys.Add(key);
                    if (scryfallCard != null) _frontArtLibrary.ApplyMetadata(entry.Id, scryfallCard);
                    _frontArtLibrary.ApplyMpcFillDefaults(entry.Id, source);
                }
                else skipped++;
            }
        }
        finally { _frontArtLibrary.EndBatch(); }

        if (newEntries.Count > 0 && _frontThumbnails != null)
        {
            StatusLabel.Text = $"Generating thumbnails for {newEntries.Count} new image(s)...";
            var thumbSvc = _frontThumbnails;
            await Task.Run(() => thumbSvc.RegenerateAll(newEntries,
                onProgress: (done, total) =>
                    Dispatcher.UIThread.InvokeAsync(() => StatusLabel.Text = $"Generating thumbnails {done}/{total}...")));
        }

        foreach (var key in importedCacheKeys) _imageCache.Remove(key);
        StatusLabel.Text = $"Imported {added} image(s) to library ({skipped} already existed or skipped)";
        if (added > 0) await LoadOptionsAsync();
    }

    private async void OnAddToFrontLibraryClick(object? sender, RoutedEventArgs e)
    {
        if (_frontArtLibrary == null) return;

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;

        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Add Image to Front Art Library", AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Image Files") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" } }
            }
        });

        int added = 0;
        foreach (var f in files)
        {
            var path = f.Path.LocalPath;
            if (path != null && _frontArtLibrary.AddFromFile(path) != null) added++;
        }
        StatusLabel.Text = $"Added {added} image(s) to front art library";
        await LoadOptionsAsync();
    }

    // ================================================================
    //  SEARCH & FILTER
    // ================================================================

    private void OnSearchRequested(object? sender, EventArgs e) => ApplyFilters();
    private void OnSourceChanged(object? sender, EventArgs e) => ApplyFilters();

    private void ApplyFilters()
    {
        if (_allTiles.Count == 0) return;

        string searchText = SearchBar?.SearchText ?? "";
        string sourceFilter = SearchBar?.IsAllSourcesSelected == false ? SearchBar.SelectedSource : "";

        int visible = 0, total = 0;
        foreach (var tile in _allTiles)
        {
            if (tile.IsAction) { tile.Tile.IsVisible = true; continue; }
            total++;

            bool matchesSearch = string.IsNullOrEmpty(searchText) ||
                tile.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                tile.Source.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                tile.Detail.Contains(searchText, StringComparison.OrdinalIgnoreCase);

            bool matchesSource = string.IsNullOrEmpty(sourceFilter)
                || (sourceFilter.Equals(McpFillAllFilter, StringComparison.OrdinalIgnoreCase)
                    // "MPCFill (all)" = any tile that isn't Scryfall or Library (i.e. an MPC source).
                    ? (!tile.Source.Equals("Scryfall", StringComparison.OrdinalIgnoreCase)
                       && !tile.Source.Equals("Library", StringComparison.OrdinalIgnoreCase)
                       && !string.IsNullOrEmpty(tile.Source))
                    : tile.Source.Equals(sourceFilter, StringComparison.OrdinalIgnoreCase));

            tile.Tile.IsVisible = matchesSearch && matchesSource;
            if (tile.Tile.IsVisible) visible++;
        }

        if (!string.IsNullOrEmpty(searchText) || !string.IsNullOrEmpty(sourceFilter))
            StatusLabel.Text = $"Showing {visible} of {total} option(s)";
    }

    // Synthetic dropdown entry that matches every MPCFill source at once.
    private const string McpFillAllFilter = "MPCFill (all)";

    private void PopulateSourceFilter()
    {
        var distinct = _allTiles
            .Where(t => !t.IsAction && !string.IsNullOrEmpty(t.Source))
            .Select(t => t.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool hasScryfall = distinct.Any(s => s.Equals("Scryfall", StringComparison.OrdinalIgnoreCase));
        bool hasLibrary = distinct.Any(s => s.Equals("Library", StringComparison.OrdinalIgnoreCase));
        var mpcSources = distinct
            .Where(s => !s.Equals("Scryfall", StringComparison.OrdinalIgnoreCase)
                     && !s.Equals("Library", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Order: Scryfall, MPCFill (all), each MPC source, Library. ("All Sources" = no filter.)
        var ordered = new List<string>();
        if (hasScryfall) ordered.Add("Scryfall");
        if (mpcSources.Count > 0) ordered.Add(McpFillAllFilter);
        ordered.AddRange(mpcSources);
        if (hasLibrary) ordered.Add("Library");

        SearchBar.SetSources(ordered, "All Sources");
    }

    // ================================================================
    //  FILTER PANEL
    // ================================================================

    private void OnToggleFilters(object? sender, RoutedEventArgs e)
    {
        _filterPanelVisible = !_filterPanelVisible;
        FilterPanel.IsVisible = _filterPanelVisible;
        FilterToggleBtn.Content = _filterPanelVisible ? "Filters ▾" : "Filters ▸";
    }

    private void LoadFilterControls(MpcFillSearchOptions opts)
    {
        SelectComboByTag(FilterSortByBox, opts.SortBy);
        SelectComboByTag(FilterMinDpiBox, opts.MinimumDpi.ToString());
        SelectComboByTag(FilterMaxDpiBox, opts.MaximumDpi.ToString());
        FilterMaxSizeBox.Text = opts.MaximumSize.ToString();
        FilterFuzzyBox.IsChecked = opts.FuzzySearch;
        FilterCardbacksBox.IsChecked = opts.FilterCardbacks;

        FilterTypeCard.IsChecked = opts.CardTypes.Contains("CARD");
        FilterTypeToken.IsChecked = opts.CardTypes.Contains("TOKEN");
        FilterTypeCardback.IsChecked = opts.CardTypes.Contains("CARDBACK");

        FilterLangEN.IsChecked = opts.Languages.Contains("EN");
        FilterLangJA.IsChecked = opts.Languages.Contains("JA");
        FilterLangFR.IsChecked = opts.Languages.Contains("FR");
        FilterLangDE.IsChecked = opts.Languages.Contains("DE");
        FilterLangES.IsChecked = opts.Languages.Contains("ES");
        FilterLangIT.IsChecked = opts.Languages.Contains("IT");
        FilterLangPT.IsChecked = opts.Languages.Contains("PT");
        FilterLangZH.IsChecked = opts.Languages.Contains("ZH");
        FilterLangRU.IsChecked = opts.Languages.Contains("RU");
        FilterLangAR.IsChecked = opts.Languages.Contains("AR");
        FilterLangSA.IsChecked = opts.Languages.Contains("SA");

        FilterExcludeNsfw.IsChecked = opts.ExcludesTags.Contains("NSFW");
        FilterExcludeAiArt.IsChecked = opts.ExcludesTags.Contains("AI Art");
    }

    private MpcFillSearchOptions BuildSearchOptionsFromControls()
    {
        var cardTypes = new List<string>();
        if (FilterTypeCard.IsChecked == true) cardTypes.Add("CARD");
        if (FilterTypeToken.IsChecked == true) cardTypes.Add("TOKEN");
        if (FilterTypeCardback.IsChecked == true) cardTypes.Add("CARDBACK");
        if (cardTypes.Count == 0) cardTypes.Add("CARD");

        var languages = new List<string>();
        if (FilterLangEN.IsChecked == true) languages.Add("EN");
        if (FilterLangJA.IsChecked == true) languages.Add("JA");
        if (FilterLangFR.IsChecked == true) languages.Add("FR");
        if (FilterLangDE.IsChecked == true) languages.Add("DE");
        if (FilterLangES.IsChecked == true) languages.Add("ES");
        if (FilterLangIT.IsChecked == true) languages.Add("IT");
        if (FilterLangPT.IsChecked == true) languages.Add("PT");
        if (FilterLangZH.IsChecked == true) languages.Add("ZH");
        if (FilterLangRU.IsChecked == true) languages.Add("RU");
        if (FilterLangAR.IsChecked == true) languages.Add("AR");
        if (FilterLangSA.IsChecked == true) languages.Add("SA");

        var excludeTags = new List<string>();
        if (FilterExcludeNsfw.IsChecked == true) excludeTags.Add("NSFW");
        if (FilterExcludeAiArt.IsChecked == true) excludeTags.Add("AI Art");

        return new MpcFillSearchOptions
        {
            CardTypes = cardTypes.ToArray(),
            SortBy = GetComboTag(FilterSortByBox) ?? "nameAscending",
            MinimumDpi = int.TryParse(GetComboTag(FilterMinDpiBox), out var minD) ? minD : 0,
            MaximumDpi = int.TryParse(GetComboTag(FilterMaxDpiBox), out var maxD) ? maxD : 1500,
            MaximumSize = int.TryParse(FilterMaxSizeBox.Text, out var ms) && ms > 0 ? ms : 30,
            FuzzySearch = FilterFuzzyBox.IsChecked == true,
            FilterCardbacks = FilterCardbacksBox.IsChecked == true,
            Languages = languages.ToArray(),
            IncludesTags = Array.Empty<string>(),
            ExcludesTags = excludeTags.ToArray()
        };
    }

    private void OnClearFilters(object? sender, RoutedEventArgs e)
        => LoadFilterControls(new MpcFillSearchOptions());

    private async void OnResearchMpcFill(object? sender, RoutedEventArgs e)
    {
        _mpcSearchOptions = BuildSearchOptionsFromControls();
        _apiResultsCache.TryRemove(_card.Name, out _); // force a fresh search with the new filters
        _scryfallCardsByPath.Clear();
        OkBtn.IsEnabled = false;
        ResultPath = null;
        PreviewPanel.Clear();
        SpinnerDot.IsVisible = true;
        await LoadOptionsAsync();
    }

    // ================================================================
    //  OK / CANCEL
    // ================================================================

    private async void OnOk(object? sender, RoutedEventArgs e)
    {
        // Upgrade normal-size Scryfall thumbnail to full-size
        if (ResultPath != null && _scryfallCardsByPath.TryGetValue(ResultPath, out var sc))
        {
            OkBtn.IsEnabled = false;
            StatusLabel.Text = "Downloading full resolution...";
            var fullPath = await _scryfall.DownloadAndCacheImageAsync(sc, size: "large");
            if (fullPath != null) ResultPath = fullPath;
            OkBtn.IsEnabled = true;
        }

        ApplyToThisCopyOnly = ApplyThisCopyChk.IsChecked == true;
        ApplyToSameName = !ApplyToThisCopyOnly && ApplySameNameChk.IsChecked == true;
        ApplyToNoBack = !ApplyToThisCopyOnly && ApplyNoBackChk.IsChecked == true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    // ----------------------------------------------------------------

    private static void SelectComboByTag(ComboBox box, string tagValue)
    {
        for (int i = 0; i < box.ItemCount; i++)
        {
            if (box.Items[i] is ComboBoxItem item && item.Tag?.ToString() == tagValue)
            {
                box.SelectedIndex = i;
                return;
            }
        }
        if (box.ItemCount > 0) box.SelectedIndex = box.ItemCount - 1;
    }

    private static string? GetComboTag(ComboBox box)
        => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString();
}
