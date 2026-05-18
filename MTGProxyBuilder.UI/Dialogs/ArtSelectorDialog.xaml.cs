using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.Dialogs
{
    public enum ArtSelectorMode { Front, Back }

    public partial class ArtSelectorDialog : Window
    {
        private readonly CardModel _card;
        private readonly ArtSelectorMode _mode;
        private readonly ScryfallService _scryfall;
        private readonly MpcFillService _mpcFill;
        private readonly ImageCacheService _imageCache;
        private readonly BackArtLibraryService? _backLibrary;
        private readonly IList<CardModel>? _allCards;
        private readonly object[][]? _mpcSourcesOverride;
        private readonly FrontArtLibraryService? _frontArtLibrary;
        private MpcFillSearchOptions _mpcSearchOptions;

        public string? ResultPath { get; private set; }

        // Maps normal-size Scryfall tile paths to their card data for full-size upgrade on selection
        private readonly Dictionary<string, ScryfallCard> _scryfallCardsByPath = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>When true, the result should be applied to all cards with matching name.</summary>
        public bool ApplyToSameName { get; private set; }

        /// <summary>When true, the result should be applied to all cards without back art.</summary>
        public bool ApplyToNoBack { get; private set; }

        public ArtSelectorDialog(
            CardModel card,
            ArtSelectorMode mode,
            ScryfallService scryfall,
            MpcFillService mpcFill,
            ImageCacheService imageCache,
            BackArtLibraryService? backLibrary = null,
            IList<CardModel>? allCards = null,
            object[][]? mpcSourcesOverride = null,
            MpcFillSearchOptions? mpcSearchOptions = null,
            FrontArtLibraryService? frontArtLibrary = null)
        {
            InitializeComponent();
            _card = card;
            _mode = mode;
            _scryfall = scryfall;
            _mpcFill = mpcFill;
            _imageCache = imageCache;
            _backLibrary = backLibrary;
            _allCards = allCards;
            _mpcSourcesOverride = mpcSourcesOverride;
            _mpcSearchOptions = mpcSearchOptions ?? new MpcFillSearchOptions();
            _frontArtLibrary = frontArtLibrary;

            bool isFront = mode == ArtSelectorMode.Front;
            TitleLabel.Text = isFront ? "Select Front Artwork" : "Select Card Back";
            CardNameLabel.Text = $"for: {card.Name}";

            // Set up bulk action buttons
            if (isFront && _allCards != null)
            {
                int sameNameCount = _allCards.Count(c => c.Name == card.Name);
                if (sameNameCount > 1)
                {
                    ApplySameNameChk.Content = $"Apply to all \"{card.Name}\" ({sameNameCount} cards)";
                    ApplySameNameChk.Visibility = Visibility.Visible;
                }
            }

            if (!isFront && _allCards != null)
            {
                int noBackCount = _allCards.Count(c => string.IsNullOrEmpty(c.BackArtworkPath));
                if (noBackCount > 0)
                {
                    ApplyNoBackChk.Content = $"Apply to all without back art ({noBackCount} cards)";
                    ApplyNoBackChk.Visibility = Visibility.Visible;
                }
            }

            // Show actions bar in front mode with library available
            if (isFront && _frontArtLibrary != null)
                ActionsBar.Visibility = Visibility.Visible;

            LoadFilterControls(_mpcSearchOptions);
            Loaded += async (_, _) => await LoadOptionsAsync();
        }

        private async Task LoadOptionsAsync()
        {
            OptionsPanel.Children.Clear();
            var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool isFront = _mode == ArtSelectorMode.Front;

            // 1. Current artwork
            string? currentPath = isFront ? _card.ArtworkPath : _card.BackArtworkPath;
            if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
            {
                AddOption("Current", currentPath, true, "Currently assigned");
                shown.Add(currentPath);
            }

            if (isFront)
            {
                await LoadFrontOptions(shown);
            }
            else
            {
                await LoadBackOptionsAsync(shown);
            }

            StatusLabel.Text = $"{shown.Count} option(s) found";
            SpinnerDot.Visibility = Visibility.Collapsed;
        }

        private async Task LoadFrontOptions(HashSet<string> shown)
        {
            if (string.IsNullOrEmpty(_card.Name))
            {
                AddActionTile("Browse File...", OnBrowseFile);
                return;
            }

            // 1. Show local library matches first (instant, no network)
            var libraryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_frontArtLibrary != null)
            {
                var libraryMatches = _frontArtLibrary.SearchByCardName(_card.Name);
                if (libraryMatches.Count > 0)
                {
                    StatusLabel.Text = $"Found {libraryMatches.Count} in library, searching online...";
                    foreach (var m in libraryMatches)
                        libraryNames.Add(m.Name);
                    var deferredImages = new List<(Image img, string path)>();
                    foreach (var entry in libraryMatches)
                    {
                        if (shown.Contains(entry.FilePath)) continue;
                        shown.Add(entry.FilePath);

                        var border = new Border
                        {
                            Width = 110, Height = 165, Margin = new Thickness(4),
                            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                            CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                            BorderThickness = new Thickness(2),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                            ToolTip = $"{entry.Name}\nLibrary | {entry.Source}"
                        };
                        var stack = new StackPanel();
                        var imgBorder = new Border
                        {
                            Height = 125, Background = Brushes.Black,
                            CornerRadius = new CornerRadius(3, 3, 0, 0), ClipToBounds = true
                        };
                        var img = new Image { Stretch = Stretch.UniformToFill };
                        imgBorder.Child = img;
                        deferredImages.Add((img, entry.FilePath));
                        stack.Children.Add(imgBorder);

                        var lbl = new TextBlock
                        {
                            Text = "\u2605 " + entry.Name,
                            Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                            FontSize = 9.5, TextTrimming = TextTrimming.CharacterEllipsis,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(3, 4, 3, 0)
                        };
                        stack.Children.Add(lbl);
                        var detailLbl = new TextBlock
                        {
                            Text = $"Library | {entry.Source}",
                            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                            FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(3, 0, 3, 2)
                        };
                        stack.Children.Add(detailLbl);
                        border.Child = stack;

                        string path = entry.FilePath;
                        string capturedName = entry.Name;
                        string detail = $"Library | {entry.Source}";
                        border.MouseLeftButtonUp += (_, _) => SelectOption(capturedName, path, detail, border);
                        border.MouseLeftButtonDown += (_, ev) =>
                        {
                            if (ev.ClickCount == 2) { SelectOption(capturedName, path, detail, border); OkClick(null!, null!); }
                        };

                        OptionsPanel.Children.Add(border);
                    }

                    // Load library thumbnails progressively
                    if (deferredImages.Count > 0)
                    {
                        const int batchSize = 20;
                        for (int i = 0; i < deferredImages.Count; i += batchSize)
                        {
                            var batch = deferredImages.Skip(i).Take(batchSize).ToList();
                            var bitmaps = await Task.Run(() =>
                            {
                                var results = new List<BitmapImage?>();
                                foreach (var (_, path) in batch)
                                {
                                    try
                                    {
                                        var bmp = new BitmapImage();
                                        bmp.BeginInit();
                                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                                        bmp.DecodePixelWidth = 150;
                                        bmp.EndInit();
                                        bmp.Freeze();
                                        results.Add(bmp);
                                    }
                                    catch { results.Add(null); }
                                }
                                return results;
                            });
                            for (int j = 0; j < batch.Count && j < bitmaps.Count; j++)
                                if (bitmaps[j] != null) batch[j].img.Source = bitmaps[j];
                        }
                    }
                }
            }

            // 2. Kick off API searches concurrently
            StatusLabel.Text = $"Searching for \"{_card.Name}\"...";
            var mpcOpts = BuildSearchOptionsFromControls();
            mpcOpts.FuzzySearch = false;
            var scryfallTask = Task.Run(async () =>
            {
                try { return (await _scryfall.SearchCardAsync($"!\"{_card.Name}\"")).Cards; }
                catch { return new List<ScryfallCard>(); }
            });
            var mpcTask = Task.Run(async () =>
            {
                try
                {
                    var (results, _) = await _mpcFill.SearchAsync(
                        _card.Name, fuzzySearch: false, sourcesOverride: _mpcSourcesOverride,
                        options: mpcOpts);
                    return results
                        .Where(mc => mc.Name.Contains(_card.Name, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }
                catch { return new List<MpcFillCard>(); }
            });

            await Task.WhenAll(scryfallTask, mpcTask);
            var scryfallResults = scryfallTask.Result;
            var mpcResults = mpcTask.Result;

            // Skip MPCFill results that are already in the local library
            if (libraryNames.Count > 0)
                mpcResults = mpcResults
                    .Where(mc => !libraryNames.Contains($"{mc.Name} [{mc.Source}]"))
                    .ToList();

            int totalImages = scryfallResults.Count + mpcResults.Count;

            // Warn the user if there are a lot of results to cache
            if (totalImages > 200)
            {
                var answer = MessageBox.Show(
                    $"Found {totalImages} art options ({scryfallResults.Count} Scryfall, {mpcResults.Count} MPCFill).\n\n" +
                    "Downloading and caching all of these may take a while. Continue?",
                    "Large Number of Results",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                {
                    StatusLabel.Text = $"{totalImages} results found (download skipped)";
                    SpinnerDot.Visibility = Visibility.Collapsed;
                    AddActionTile("Browse File...", OnBrowseFile);
                    return;
                }
            }

            // Download all images in parallel
            int completed = 0;
            var semaphore = new System.Threading.SemaphoreSlim(8);

            var scryfallDownloads = scryfallResults
                .Where(sc => sc.GetImageUrl() != null)
                .Select(async sc =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var cached = await _scryfall.DownloadAndCacheImageAsync(sc, size: "normal");
                        var done = System.Threading.Interlocked.Increment(ref completed);
                        _ = Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Downloading art {done}/{totalImages}...");
                        if (cached != null)
                            return (Label: $"{sc.SetName} #{sc.CollectorNumber}",
                                    Path: cached,
                                    Detail: $"Scryfall | {sc.Artist ?? ""}",
                                    ScryfallCard: (ScryfallCard?)sc,
                                    MpcSource: (string?)null);
                        return default;
                    }
                    finally { semaphore.Release(); }
                }).ToList();

            var mpcDownloads = mpcResults.Select(async mc =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var cached = await _mpcFill.DownloadAndCacheImageAsync(mc);
                    var done = System.Threading.Interlocked.Increment(ref completed);
                    _ = Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Downloading art {done}/{totalImages}...");
                    if (cached != null)
                        return (Label: mc.Name,
                                Path: cached,
                                Detail: $"MPCFill | {mc.Source} | {mc.Dpi} DPI",
                                ScryfallCard: (ScryfallCard?)null,
                                MpcSource: (string?)mc.Source);
                    return default;
                }
                finally { semaphore.Release(); }
            }).ToList();

            var allDownloads = scryfallDownloads.Concat(mpcDownloads).ToList();
            var downloadResults = await Task.WhenAll(allDownloads);

            // Add tiles (Scryfall first, then MPCFill)
            foreach (var result in downloadResults)
            {
                if (result.Path != null && !shown.Contains(result.Path))
                {
                    AddOption(result.Label, result.Path, false, result.Detail, result.MpcSource);
                    shown.Add(result.Path);
                    if (result.ScryfallCard != null)
                        _scryfallCardsByPath[result.Path] = result.ScryfallCard;
                }
            }

            // "Browse File" action tile only shown when no actions bar (back mode)
            if (_frontArtLibrary == null)
                AddActionTile("Browse File...", OnBrowseFile);
        }

        private async Task LoadBackOptionsAsync(HashSet<string> shown)
        {
            // Original Scryfall back (if card was double-faced)
            if (!string.IsNullOrEmpty(_card.OriginalBackArtworkPath)
                && File.Exists(_card.OriginalBackArtworkPath)
                && !shown.Contains(_card.OriginalBackArtworkPath))
            {
                AddOption("Original (Scryfall)", _card.OriginalBackArtworkPath, false, "From Scryfall import");
                shown.Add(_card.OriginalBackArtworkPath);
            }

            // Library entries — build tiles instantly with deferred image loading
            var deferredImages = new List<(Image img, string path)>();

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
                        Width = 110, Height = 165, Margin = new Thickness(4),
                        Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                        CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                        BorderThickness = new Thickness(2),
                        BorderBrush = isDefault ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)) : Brushes.Transparent,
                        ToolTip = $"{entry.Name}\n{(isDefault ? "DEFAULT\n" : "")}From library"
                    };

                    var stack = new StackPanel();

                    var imgBorder = new Border
                    {
                        Height = 125, Background = Brushes.Black,
                        CornerRadius = new CornerRadius(3, 3, 0, 0), ClipToBounds = true
                    };
                    var img = new Image { Stretch = Stretch.UniformToFill };
                    imgBorder.Child = img;
                    deferredImages.Add((img, entry.FilePath));
                    stack.Children.Add(imgBorder);

                    var lbl = new TextBlock
                    {
                        Text = (isDefault ? "\u2605 " : "") + entry.Name,
                        Foreground = isDefault
                            ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                            : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                        FontSize = 9.5, TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(3, 4, 3, 0)
                    };
                    stack.Children.Add(lbl);

                    var detailLbl = new TextBlock
                    {
                        Text = "From library",
                        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                        FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(3, 0, 3, 2)
                    };
                    stack.Children.Add(detailLbl);

                    border.Child = stack;

                    string path = entry.FilePath;
                    string capturedName = entry.Name;
                    border.MouseLeftButtonUp += (_, _) => SelectOption(capturedName, path, "From library", border);
                    border.MouseLeftButtonDown += (_, ev) =>
                    {
                        if (ev.ClickCount == 2) { SelectOption(capturedName, path, "From library", border); OkClick(null!, null!); }
                    };
                    border.MouseRightButtonUp += (_, ev) =>
                    {
                        var menu = new System.Windows.Controls.ContextMenu();
                        var previewItem = new System.Windows.Controls.MenuItem { Header = "Preview Full Size" };
                        previewItem.Click += (_, _) =>
                        {
                            var preview = new ImagePreviewDialog(path, capturedName);
                            preview.Owner = this;
                            preview.ShowDialog();
                        };
                        menu.Items.Add(previewItem);
                        menu.IsOpen = true;
                        ev.Handled = true;
                    };

                    OptionsPanel.Children.Add(border);
                }
            }

            // Action tiles
            AddActionTile("Download MPCFill\nCard Backs", OnDownloadMpcFillBacks);
            if (_backLibrary != null)
                AddActionTile("+ Add to Library", OnAddToLibrary);
            AddActionTile("Browse File...", OnBrowseFile);

            StatusLabel.Text = $"{shown.Count} option(s) found";

            // Load thumbnails progressively on background thread
            if (deferredImages.Count > 0)
            {
                StatusLabel.Text = $"{shown.Count} option(s) — loading thumbnails...";
                const int batchSize = 20;
                for (int i = 0; i < deferredImages.Count; i += batchSize)
                {
                    var batch = deferredImages.Skip(i).Take(batchSize).ToList();
                    var bitmaps = await Task.Run(() =>
                    {
                        var results = new List<BitmapImage?>();
                        foreach (var (_, path) in batch)
                        {
                            try
                            {
                                var bmp = new BitmapImage();
                                bmp.BeginInit();
                                bmp.UriSource = new Uri(path, UriKind.Absolute);
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.DecodePixelWidth = 150;
                                bmp.EndInit();
                                bmp.Freeze();
                                results.Add(bmp);
                            }
                            catch { results.Add(null); }
                        }
                        return results;
                    });

                    for (int j = 0; j < batch.Count && j < bitmaps.Count; j++)
                        if (bitmaps[j] != null) batch[j].img.Source = bitmaps[j];
                }
                StatusLabel.Text = $"{shown.Count} option(s) found";
            }
        }

        private async void OnDownloadMpcFillBacks()
        {
            if (_backLibrary == null) return;

            StatusLabel.Text = "Fetching card back list from MPCFill...";
            SpinnerDot.Visibility = Visibility.Visible;

            try
            {
                var (cardbacks, error) = await _mpcFill.SearchCardbacksAsync(500);
                if (error != null || cardbacks.Count == 0)
                {
                    StatusLabel.Text = error ?? "No card backs found on MPCFill.";
                    SpinnerDot.Visibility = Visibility.Collapsed;
                    return;
                }

                StatusLabel.Text = $"Downloading {cardbacks.Count} card backs...";
                var results = await _mpcFill.DownloadAndCacheImagesAsync(
                    cardbacks,
                    maxConcurrency: 8,
                    onProgress: (done, total, name) =>
                        Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Downloading card back {done}/{total}: {name}..."));

                int added = 0;
                int skipped = 0;
                _backLibrary.BeginBatch();
                try
                {
                    foreach (var (cb, cached) in results)
                    {
                        if (cached == null) { skipped++; continue; }
                        string displayName = $"{cb.Name} [{cb.Source}]";
                        var entry = _backLibrary.AddFromFile(cached, displayName, cb.Source);
                        if (entry != null) added++;
                        else skipped++;
                    }
                }
                finally { _backLibrary.EndBatch(); }

                StatusLabel.Text = $"Added {added} card back(s) to library ({skipped} already existed or failed)";

                // Rebuild the dialog options to show the new library entries
                var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                OptionsPanel.Children.Clear();

                string? currentPath = _card.BackArtworkPath;
                if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
                {
                    AddOption("Current", currentPath, true, "Currently assigned");
                    shown.Add(currentPath);
                }
                await LoadBackOptionsAsync(shown);
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error: {ex.Message}";
            }
            finally
            {
                SpinnerDot.Visibility = Visibility.Collapsed;
            }
        }

        // ================================================================
        //  TILE BUILDERS
        // ================================================================

        private void AddOption(string label, string imagePath, bool isCurrent, string detail, string? mpcSource = null)
        {
            var border = new Border
            {
                Width = 110, Height = 165, Margin = new Thickness(4),
                Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(2),
                BorderBrush = isCurrent ? Brushes.DodgerBlue : Brushes.Transparent,
                ToolTip = $"{label}\n{detail}"
            };

            var stack = new StackPanel();

            var imgBorder = new Border
            {
                Height = 125, Background = Brushes.Black,
                CornerRadius = new CornerRadius(3, 3, 0, 0), ClipToBounds = true
            };
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(imagePath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 220;
                bmp.EndInit();
                bmp.Freeze();
                imgBorder.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
            }
            catch
            {
                imgBorder.Child = new TextBlock
                {
                    Text = "?", Foreground = Brushes.Gray, FontSize = 24,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            stack.Children.Add(imgBorder);

            var lbl = new TextBlock
            {
                Text = label + (isCurrent ? " *" : ""),
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 9.5, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(3, 4, 3, 0)
            };
            stack.Children.Add(lbl);

            var detailLbl = new TextBlock
            {
                Text = detail, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 8, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(3, 0, 3, 2)
            };
            stack.Children.Add(detailLbl);

            border.Child = stack;

            string path = imagePath;
            string capturedLabel = label;
            string capturedDetail = detail;
            border.MouseLeftButtonUp += (_, _) => SelectOption(capturedLabel, path, capturedDetail, border);
            border.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                {
                    SelectOption(capturedLabel, path, capturedDetail, border);
                    OkClick(null!, null!);
                }
            };
            border.MouseRightButtonUp += (_, e) =>
            {
                var menu = new System.Windows.Controls.ContextMenu();
                var previewItem = new System.Windows.Controls.MenuItem { Header = "Preview Full Size" };
                previewItem.Click += (_, _) =>
                {
                    var preview = new ImagePreviewDialog(path, capturedLabel);
                    preview.Owner = this;
                    preview.ShowDialog();
                };
                menu.Items.Add(previewItem);

                if (_frontArtLibrary != null && mpcSource != null)
                {
                    var saveItem = new System.Windows.Controls.MenuItem { Header = "Save to Library" };
                    saveItem.Click += (_, _) =>
                    {
                        string libName = $"{capturedLabel} [{mpcSource}]";
                        var entry = _frontArtLibrary.AddFromFile(path, libName, mpcSource);
                        StatusLabel.Text = entry != null
                            ? $"Saved \"{libName}\" to front art library"
                            : $"\"{libName}\" already in library";
                    };
                    menu.Items.Add(saveItem);
                }

                menu.IsOpen = true;
                e.Handled = true;
            };

            OptionsPanel.Children.Add(border);
        }

        private void AddActionTile(string label, Action action)
        {
            var border = new Border
            {
                Width = 110, Height = 165, Margin = new Thickness(4),
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38)),
                CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
            };

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(new TextBlock
            {
                Text = "+", FontSize = 28, Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = label, FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });
            border.Child = stack;
            border.MouseLeftButtonUp += (_, _) => action();
            OptionsPanel.Children.Add(border);
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
        //  ACTIONS
        // ================================================================

        private void OnImportCacheToLibrary(object sender, RoutedEventArgs e) => OnImportCacheToLibrary();
        private void OnAddToFrontLibraryClick(object sender, RoutedEventArgs e) => OnAddToFrontLibrary();
        private void OnBrowseFileClick(object sender, RoutedEventArgs e) => OnBrowseFile();

        private void OnImportCacheToLibrary()
        {
            if (_frontArtLibrary == null) return;

            var cached = _imageCache.GetCachedByPrefix("mpc_");
            if (cached.Count == 0)
            {
                StatusLabel.Text = "No downloaded MPCFill art found in cache.";
                return;
            }

            int added = 0, skipped = 0;
            _frontArtLibrary.BeginBatch();
            try
            {
                foreach (var (key, path, name, source) in cached)
                {
                    if (!File.Exists(path)) { skipped++; continue; }
                    string displayName = !string.IsNullOrEmpty(source)
                        ? $"{name} [{source}]" : name;
                    if (_frontArtLibrary.AddFromFile(path, displayName, source) != null) added++;
                    else skipped++;
                }
            }
            finally { _frontArtLibrary.EndBatch(); }

            StatusLabel.Text = $"Imported {added} image(s) to library ({skipped} already existed or skipped)";
            if (added > 0)
                _ = LoadOptionsAsync();
        }

        private void OnAddToFrontLibrary()
        {
            if (_frontArtLibrary == null) return;
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Add Image to Front Art Library",
                Multiselect = true
            };
            if (dialog.ShowDialog() != true) return;
            int added = 0;
            foreach (var file in dialog.FileNames)
            {
                if (_frontArtLibrary.AddFromFile(file) != null) added++;
            }
            StatusLabel.Text = $"Added {added} image(s) to front art library";
            _ = LoadOptionsAsync(); // rebuild to show new entries
        }

        private void OnAddToLibrary()
        {
            if (_backLibrary == null) return;
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Add Image to Back Art Library"
            };
            if (dialog.ShowDialog() != true) return;
            _backLibrary.AddFromFile(dialog.FileName);
            _ = LoadOptionsAsync(); // rebuild
        }

        private void OnBrowseFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Select Artwork"
            };
            if (dialog.ShowDialog() != true) return;

            ResultPath = dialog.FileName;
            OkBtn.IsEnabled = true;
            PreviewPanel.ShowImage(dialog.FileName, Path.GetFileName(dialog.FileName), "Local file");
        }

        // ================================================================
        //  FILTER PANEL
        // ================================================================

        private void LoadFilterControls(MpcFillSearchOptions opts)
        {
            SelectByTag(FilterSortByBox, opts.SortBy);
            SelectByTag(FilterMinDpiBox, opts.MinimumDpi.ToString());
            SelectByTag(FilterMaxDpiBox, opts.MaximumDpi.ToString());
            FilterMaxSizeBox.Text = opts.MaximumSize.ToString();
            FilterFuzzyBox.IsChecked = opts.FuzzySearch;
            FilterCardbacksBox.IsChecked = opts.FilterCardbacks;

            FilterTypeCard.IsChecked = opts.CardTypes.Contains("CARD");
            FilterTypeToken.IsChecked = opts.CardTypes.Contains("TOKEN");
            FilterTypeCardback.IsChecked = opts.CardTypes.Contains("CARDBACK");

            var langs = opts.Languages;
            FilterLangEN.IsChecked = langs.Contains("EN");
            FilterLangJA.IsChecked = langs.Contains("JA");
            FilterLangFR.IsChecked = langs.Contains("FR");
            FilterLangDE.IsChecked = langs.Contains("DE");
            FilterLangES.IsChecked = langs.Contains("ES");
            FilterLangIT.IsChecked = langs.Contains("IT");
            FilterLangPT.IsChecked = langs.Contains("PT");
            FilterLangZH.IsChecked = langs.Contains("ZH");
            FilterLangRU.IsChecked = langs.Contains("RU");
            FilterLangAR.IsChecked = langs.Contains("AR");
            FilterLangSA.IsChecked = langs.Contains("SA");

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
                SortBy = (FilterSortByBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "nameAscending",
                MinimumDpi = int.TryParse((FilterMinDpiBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var minD) ? minD : 0,
                MaximumDpi = int.TryParse((FilterMaxDpiBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var maxD) ? maxD : 1500,
                MaximumSize = int.TryParse(FilterMaxSizeBox.Text, out var ms) && ms > 0 ? ms : 30,
                FuzzySearch = FilterFuzzyBox.IsChecked == true,
                FilterCardbacks = FilterCardbacksBox.IsChecked == true,
                Languages = languages.ToArray(),
                IncludesTags = Array.Empty<string>(),
                ExcludesTags = excludeTags.ToArray()
            };
        }

        private static void SelectByTag(ComboBox box, string tagValue)
        {
            foreach (ComboBoxItem item in box.Items)
            {
                if (item.Tag?.ToString() == tagValue)
                {
                    box.SelectedItem = item;
                    return;
                }
            }
            box.SelectedIndex = box.Items.Count - 1;
        }

        private void OnClearFilters(object sender, RoutedEventArgs e)
        {
            LoadFilterControls(new MpcFillSearchOptions());
        }

        private async void OnResearchMpcFill(object sender, RoutedEventArgs e)
        {
            _mpcSearchOptions = BuildSearchOptionsFromControls();
            _scryfallCardsByPath.Clear();
            OkBtn.IsEnabled = false;
            ResultPath = null;
            PreviewPanel.Clear();
            SpinnerDot.Visibility = Visibility.Visible;
            await LoadOptionsAsync();
        }

        private async void OkClick(object sender, RoutedEventArgs e)
        {
            // If the selected path is a normal-size Scryfall thumbnail, upgrade to full-size
            if (ResultPath != null && _scryfallCardsByPath.TryGetValue(ResultPath, out var sc))
            {
                OkBtn.IsEnabled = false;
                StatusLabel.Text = "Downloading full resolution...";
                var fullPath = await _scryfall.DownloadAndCacheImageAsync(sc, size: "large");
                if (fullPath != null)
                    ResultPath = fullPath;
                OkBtn.IsEnabled = true;
            }

            ApplyToSameName = ApplySameNameChk.IsChecked == true;
            ApplyToNoBack = ApplyNoBackChk.IsChecked == true;
            DialogResult = true;
        }
    }
}
