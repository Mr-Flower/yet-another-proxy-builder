using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using Microsoft.Win32;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.Services;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class FrontArtLibraryDialog : Window
    {
        private readonly FrontArtLibraryService _library;
        private readonly ImageCacheService? _imageCache;
        private readonly AppSettingsService? _appSettings;
        private readonly ScryfallService? _scryfall;
        private ThumbnailService _thumbnails;
        private readonly HashSet<string> _selectedEntryIds = new();
        private readonly List<string> _displayedEntryIds = new();
        private int _anchorIndex = -1;

        public FrontArtLibraryDialog(FrontArtLibraryService library, ImageCacheService? imageCache = null,
            AppSettingsService? appSettings = null, ScryfallService? scryfall = null)
        {
            InitializeComponent();
            _library = library;
            _imageCache = imageCache;
            _appSettings = appSettings;
            _scryfall = scryfall;
            _thumbnails = new ThumbnailService(library.LibraryDirectory);
            ImportCacheBtn.Visibility = _imageCache != null ? Visibility.Visible : Visibility.Collapsed;
            PopulateSourceFilter();
            RefreshGrid();
        }

        private void PopulateSourceFilter()
        {
            var sources = _library.Entries
                .Select(e => e.Source)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s);

            SearchBar.SetSources(sources, "All Sources");
        }

        private void OnSearchRequested(object? sender, EventArgs e) => RefreshGrid();
        private void OnSourceChanged(object? sender, EventArgs e) => RefreshGrid();

        private void RefreshGrid()
        {
            LibraryPanel.Children.Clear();
            _selectedEntryIds.Clear();
            _displayedEntryIds.Clear();
            _anchorIndex = -1;
            RemoveBtn.IsEnabled = false;
            RemoveBtn.Content = "Remove Selected";

            var entries = _library.Entries.Where(e => File.Exists(e.FilePath)).AsEnumerable();

            var searchPredicate = LibrarySearchParser.Parse(SearchBar.SearchText);
            entries = entries.Where(searchPredicate);

            if (!SearchBar.IsAllSourcesSelected)
            {
                string sourceFilter = SearchBar.SelectedSource;
                entries = entries.Where(e => e.Source.Equals(sourceFilter, StringComparison.OrdinalIgnoreCase));
            }

            var filteredEntries = entries.ToList();

            var imageTargets = new List<(Image img, string entryId, string path)>();

            foreach (var entry in filteredEntries)
            {
                _displayedEntryIds.Add(entry.Id);

                var border = new Border
                {
                    Width = 100, Height = 150, Margin = new Thickness(4),
                    Background = AppBrushes.TileBg,
                    CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent,
                    Tag = entry.Id,
                    ToolTip = $"{entry.Name}\nSource: {entry.Source}\nAdded: {entry.AddedDate:d}"
                };

                var stack = new StackPanel();

                var imgBorder = new Border
                {
                    Height = 115, Background = Brushes.Black,
                    CornerRadius = new CornerRadius(3, 3, 0, 0), ClipToBounds = true
                };
                var img = new Image { Stretch = Stretch.UniformToFill };
                imgBorder.Child = img;
                imageTargets.Add((img, entry.Id, entry.FilePath));
                stack.Children.Add(imgBorder);

                var lbl = new TextBlock
                {
                    Text = entry.Name,
                    Foreground = AppBrushes.TextSecondary,
                    FontSize = 9, TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(3, 4, 3, 0)
                };
                stack.Children.Add(lbl);

                if (!string.IsNullOrEmpty(entry.Source) && entry.Source != "Local")
                {
                    var srcLbl = new TextBlock
                    {
                        Text = entry.Source,
                        Foreground = AppBrushes.TextMuted,
                        FontSize = 8, TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(3, 0, 3, 2)
                    };
                    stack.Children.Add(srcLbl);
                }

                border.Child = stack;

                string entryId = entry.Id;
                string capturedName = entry.Name;
                string capturedPath = entry.FilePath;
                border.MouseLeftButtonUp += (_, _) => SelectEntry(entryId, border);
                border.MouseLeftButtonDown += (_, ev) =>
                {
                    if (ev.ClickCount == 2)
                    {
                        var preview = new ImagePreviewDialog(capturedPath, capturedName);
                        preview.Owner = this;
                        preview.ShowDialog();
                    }
                };

                LibraryPanel.Children.Add(border);
            }

            int totalCount = _library.Entries.Count(e => File.Exists(e.FilePath));
            string filterInfo = filteredEntries.Count < totalCount ? $" (showing {filteredEntries.Count} of {totalCount})" : "";
            CountLabel.Text = $"{totalCount} item(s) in library{filterInfo}";
            StatusLabel.Text = "Loading thumbnails...";

            _ = LoadThumbnailsAsync(imageTargets);
        }

        private async Task LoadThumbnailsAsync(List<(Image img, string entryId, string path)> targets)
        {
            const int batchSize = 20;
            for (int i = 0; i < targets.Count; i += batchSize)
            {
                var batch = targets.Skip(i).Take(batchSize).ToList();
                var bitmaps = await Task.Run(() =>
                {
                    var results = new List<BitmapImage?>();
                    foreach (var (_, entryId, path) in batch)
                    {
                        try
                        {
                            var loadPath = _thumbnails.GetOrCreate(entryId, path) ?? path;
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.UriSource = new Uri(loadPath, UriKind.Absolute);
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
            StatusLabel.Text = "";
        }

        private void SelectEntry(string entryId, Border clickedBorder)
        {
            bool isCtrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool isShift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            int clickedIndex = _displayedEntryIds.IndexOf(entryId);

            if (isShift && _anchorIndex >= 0 && clickedIndex >= 0)
            {
                int start = Math.Min(_anchorIndex, clickedIndex);
                int end = Math.Max(_anchorIndex, clickedIndex);
                if (!isCtrl) _selectedEntryIds.Clear();
                for (int i = start; i <= end; i++)
                    _selectedEntryIds.Add(_displayedEntryIds[i]);
            }
            else if (isCtrl)
            {
                if (!_selectedEntryIds.Remove(entryId))
                    _selectedEntryIds.Add(entryId);
                _anchorIndex = clickedIndex;
            }
            else
            {
                _selectedEntryIds.Clear();
                _selectedEntryIds.Add(entryId);
                _anchorIndex = clickedIndex;
            }

            // Update all borders
            foreach (var child in LibraryPanel.Children)
            {
                if (child is Border b && b.Tag is string id)
                    b.BorderBrush = _selectedEntryIds.Contains(id) ? Brushes.DodgerBlue : Brushes.Transparent;
            }

            RemoveBtn.IsEnabled = _selectedEntryIds.Count > 0;
            RemoveBtn.Content = _selectedEntryIds.Count > 1
                ? $"Remove Selected ({_selectedEntryIds.Count})"
                : "Remove Selected";

            // Show preview for the clicked item
            var entry = _library.GetById(entryId);
            if (entry != null && File.Exists(entry.FilePath))
            {
                string sourceInfo = !string.IsNullOrEmpty(entry.Source) && entry.Source != "Local"
                    ? $"Source: {entry.Source}\n" : "";
                string detail = $"{sourceInfo}{Path.GetFileName(entry.FilePath)}";
                PreviewPanel.ShowImage(entry.FilePath, entry.Name, detail);
            }
        }

        private void OnAddFromFile(object sender, RoutedEventArgs e)
        {
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
                if (_library.AddFromFile(file) != null) added++;
            }
            PopulateSourceFilter();
            RefreshGrid();
        }

        private async void OnImportFromCache(object sender, RoutedEventArgs e)
        {
            if (_imageCache == null) return;

            var cached = _imageCache.GetCachedByPrefix("mpc_");
            if (cached.Count == 0)
            {
                StatusLabel.Text = "No downloaded MPCFill art found in cache.";
                return;
            }

            ImportCacheBtn.IsEnabled = false;
            StatusLabel.Text = $"Importing from {cached.Count} cached file(s)...";

            int added = 0, skipped = 0;
            var newEntries = new List<(string Id, string FilePath)>();
            var importedCacheKeys = new List<string>();
            _library.BeginBatch();
            try
            {
                foreach (var (key, path, name, source) in cached)
                {
                    if (!File.Exists(path)) { skipped++; continue; }
                    string displayName = !string.IsNullOrEmpty(source)
                        ? $"{name} [{source}]" : name;
                    var entry = _library.AddFromFile(path, displayName, source);
                    if (entry != null)
                    {
                        added++;
                        newEntries.Add((entry.Id, entry.FilePath));
                        importedCacheKeys.Add(key);
                    }
                    else skipped++;
                }
            }
            finally { _library.EndBatch(); }

            // Populate metadata from Scryfall for newly added entries (one lookup per unique card name)
            if (newEntries.Count > 0 && _scryfall != null)
            {
                var scryfallCache = new Dictionary<string, ScryfallCard?>(StringComparer.OrdinalIgnoreCase);
                int looked = 0;
                for (int i = 0; i < newEntries.Count; i++)
                {
                    var entry = _library.GetById(newEntries[i].Id);
                    if (entry == null || !string.IsNullOrEmpty(entry.TypeLine)) continue;

                    string cardName = entry.Name;
                    int bracketIdx = cardName.LastIndexOf('[');
                    if (bracketIdx > 0) cardName = cardName[..bracketIdx].Trim();

                    if (!scryfallCache.TryGetValue(cardName, out var sc))
                    {
                        StatusLabel.Text = $"Looking up metadata {++looked}: {cardName}...";
                        try { sc = await _scryfall.GetCardByNameAsync(cardName); }
                        catch { sc = null; }
                        scryfallCache[cardName] = sc;
                        await Task.Delay(100);
                    }

                    if (sc != null)
                        _library.ApplyMetadata(entry.Id, sc);
                }
            }

            // Apply MPCFill defaults (SetCode=MPC, SetName=MPCFill.com, Artist=source) for entries still missing metadata
            foreach (var (id, _) in newEntries)
            {
                var entry = _library.GetById(id);
                if (entry != null)
                    _library.ApplyMpcFillDefaults(id, entry.Source);
            }

            // Generate thumbnails for newly added entries
            if (newEntries.Count > 0)
            {
                StatusLabel.Text = $"Generating thumbnails for {newEntries.Count} new image(s)...";
                await Task.Run(() => _thumbnails.RegenerateAll(newEntries,
                    onProgress: (done, total) =>
                        Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Generating thumbnails {done}/{total}...")));
            }

            // Remove imported items from cache
            foreach (var key in importedCacheKeys)
                _imageCache.Remove(key);

            if (added > 0)
            {
                PopulateSourceFilter();
                RefreshGrid();
            }
            ImportCacheBtn.IsEnabled = true;
        }

        private void OnRemoveSelected(object sender, RoutedEventArgs e)
        {
            if (_selectedEntryIds.Count == 0) return;

            string message;
            if (_selectedEntryIds.Count == 1)
            {
                var entry = _library.GetById(_selectedEntryIds.First());
                message = $"Remove \"{entry?.Name ?? "this item"}\" from the library?";
            }
            else
            {
                message = $"Remove {_selectedEntryIds.Count} items from the library?";
            }

            var result = MessageBox.Show(message, "Remove", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            int removed = 0;
            foreach (var id in _selectedEntryIds.ToList())
            {
                _thumbnails.Delete(id);
                if (_library.Remove(id)) removed++;
            }
            _selectedEntryIds.Clear();
            PopulateSourceFilter();
            RefreshGrid();
        }

        private async void OnRegenerateThumbnails(object sender, RoutedEventArgs e)
        {
            RegenThumbBtn.IsEnabled = false;
            StatusLabel.Text = "Regenerating thumbnails...";

            var entries = _library.Entries
                .Where(en => File.Exists(en.FilePath))
                .Select(en => (en.Id, en.FilePath))
                .ToList();

            int generated = await Task.Run(() =>
                _thumbnails.RegenerateAll(entries,
                    onProgress: (done, total) =>
                        Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Regenerating thumbnails {done}/{total}...")));

            RegenThumbBtn.IsEnabled = true;
            RefreshGrid();
        }

        // ================================================================
        //  LIBRARY MANAGEMENT
        // ================================================================

        private async void OnMoveLibrary(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select new location for the front art library"
            };
            if (dialog.ShowDialog() != true) return;

            string newDir = Path.Combine(dialog.FolderName, "FrontArtLibrary");
            if (string.Equals(newDir, _library.LibraryDirectory, StringComparison.OrdinalIgnoreCase))
            {
                StatusLabel.Text = "Selected directory is the same as current.";
                return;
            }

            var confirm = MessageBox.Show(
                $"Move {_library.Entries.Count} image(s) to:\n{newDir}\n\nThis will move all files and delete the old location.",
                "Move Library", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            StatusLabel.Text = "Moving library...";

            List<string>? newEntryIds = null;
            await Task.Run(() => newEntryIds = _library.MoveToDirectory(newDir,
                onProgress: (done, total) =>
                    Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Moving {done}/{total}...")));

            _thumbnails = new ThumbnailService(_library.LibraryDirectory);

            // Generate thumbnails for newly merged entries
            if (newEntryIds != null && newEntryIds.Count > 0)
            {
                var toGenerate = _library.Entries
                    .Where(e => newEntryIds.Contains(e.Id) && File.Exists(e.FilePath))
                    .Select(e => (e.Id, e.FilePath))
                    .ToList();
                if (toGenerate.Count > 0)
                {
                    StatusLabel.Text = $"Generating thumbnails for {toGenerate.Count} new image(s)...";
                    await Task.Run(() => _thumbnails.RegenerateAll(toGenerate,
                        onProgress: (done, total) =>
                            Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Generating thumbnails {done}/{total}...")));
                }
            }

            if (_appSettings != null)
            {
                _appSettings.Settings.FrontArtLibraryPath = newDir;
                _appSettings.Save();
            }

            RefreshGrid();
        }

        private async void OnExportZip(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "ZIP Archive (*.zip)|*.zip",
                Title = "Export Front Art Library",
                FileName = "FrontArtLibrary.zip"
            };
            if (dialog.ShowDialog() != true) return;

            StatusLabel.Text = "Exporting library...";

            await Task.Run(() => _library.ExportToZip(dialog.FileName,
                onProgress: (done, total) =>
                    Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Compressing {done}/{total}...")));

            StatusLabel.Text = "";
        }

        private async void OnImportZip(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "ZIP Archive (*.zip)|*.zip",
                Title = "Import Front Art Library from ZIP"
            };
            if (dialog.ShowDialog() != true) return;

            StatusLabel.Text = "Importing from ZIP...";

            int added = await Task.Run(() => _library.ImportFromZip(dialog.FileName,
                onProgress: (done, total) =>
                    Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Importing {done}/{total}...")));

            PopulateSourceFilter();
            RefreshGrid();
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
