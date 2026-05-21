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
    public partial class BackArtLibraryDialog : Window
    {
        private readonly BackArtLibraryService _library;
        private readonly MpcFillService _mpcFill;
        private readonly AppSettingsService? _appSettings;
        private ThumbnailService _thumbnails;
        private readonly HashSet<string> _selectedEntryIds = new();
        private readonly List<string> _displayedEntryIds = new();
        private string? _lastSelectedId;
        private int _anchorIndex = -1;

        public BackArtLibraryDialog(BackArtLibraryService library, MpcFillService mpcFill, AppSettingsService? appSettings = null)
        {
            InitializeComponent();
            _library = library;
            _mpcFill = mpcFill;
            _appSettings = appSettings;
            _thumbnails = new ThumbnailService(library.LibraryDirectory);
            PopulateSourceFilter();
            RefreshGrid();
        }

        private void PopulateSourceFilter()
        {
            var sources = _library.Entries
                .Select(e => e.Source)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();

            SourceFilter.Items.Clear();
            SourceFilter.Items.Add("All Contributors");
            foreach (var s in sources)
                SourceFilter.Items.Add(s);
            SourceFilter.SelectedIndex = 0;
        }

        private void OnSearchKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) RefreshGrid();
        }

        private void OnSearchClick(object sender, RoutedEventArgs e)
        {
            RefreshGrid();
        }

        private void OnSourceFilterChanged(object sender, object e)
        {
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            LibraryPanel.Children.Clear();
            _selectedEntryIds.Clear();
            _displayedEntryIds.Clear();
            _lastSelectedId = null;
            _anchorIndex = -1;
            RemoveBtn.IsEnabled = false;
            RemoveBtn.Content = "Remove Selected";
            DefaultBtn.IsEnabled = false;

            string searchQuery = SearchBox?.Text?.Trim() ?? "";
            string sourceFilter = SourceFilter?.SelectedItem as string ?? "All Contributors";

            var entries = _library.Entries.Where(e => File.Exists(e.FilePath)).AsEnumerable();

            var searchPredicate = LibrarySearchParser.Parse(searchQuery);
            entries = entries.Where(searchPredicate);

            if (sourceFilter != "All Contributors")
                entries = entries.Where(e => e.Source.Equals(sourceFilter, StringComparison.OrdinalIgnoreCase));

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
                    ToolTip = $"{entry.Name}\nAdded: {entry.AddedDate:d}"
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

                bool isDefault = _library.IsDefault(entry.Id);

                var lbl = new TextBlock
                {
                    Text = isDefault ? "\u2605 " + entry.Name : entry.Name,
                    Foreground = isDefault
                        ? AppBrushes.AccentGreen
                        : AppBrushes.TextSecondary,
                    FontSize = 9, TextTrimming = TextTrimming.CharacterEllipsis,
                    FontWeight = isDefault ? FontWeights.Bold : FontWeights.Normal,
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

                if (isDefault)
                    border.BorderBrush = AppBrushes.AccentGreen;

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

            var defaultEntry = _library.DefaultEntryId != null ? _library.GetById(_library.DefaultEntryId) : null;
            string defaultInfo = defaultEntry != null ? $" | Default: {defaultEntry.Name}" : "";
            int totalCount = _library.Entries.Count(e => File.Exists(e.FilePath));
            string filterInfo = filteredEntries.Count < totalCount ? $" (showing {filteredEntries.Count} of {totalCount})" : "";
            CountLabel.Text = $"{totalCount} item(s) in library{filterInfo}{defaultInfo}";
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

            _lastSelectedId = _selectedEntryIds.Contains(entryId) ? entryId : _selectedEntryIds.LastOrDefault();

            // Update all borders: selected = blue, default = green, else transparent
            foreach (var child in LibraryPanel.Children)
            {
                if (child is Border b && b.Tag is string id)
                {
                    if (_selectedEntryIds.Contains(id))
                        b.BorderBrush = Brushes.DodgerBlue;
                    else if (_library.IsDefault(id))
                        b.BorderBrush = AppBrushes.AccentGreen;
                    else
                        b.BorderBrush = Brushes.Transparent;
                }
            }

            RemoveBtn.IsEnabled = _selectedEntryIds.Count > 0;
            RemoveBtn.Content = _selectedEntryIds.Count > 1
                ? $"Remove Selected ({_selectedEntryIds.Count})"
                : "Remove Selected";
            DefaultBtn.IsEnabled = _lastSelectedId != null;

            // Load preview for clicked item
            var entry = _lastSelectedId != null ? _library.GetById(_lastSelectedId) : null;
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
                Title = "Add Image to Back Art Library",
                Multiselect = true
            };
            if (dialog.ShowDialog() != true) return;

            int added = 0;
            foreach (var file in dialog.FileNames)
            {
                var entry = _library.AddFromFile(file);
                if (entry != null) added++;
            }
            StatusLabel.Text = $"Added {added} image(s)";
            RefreshGrid();
        }

        private void OnSetDefault(object sender, RoutedEventArgs e)
        {
            if (_lastSelectedId == null) return;
            _library.SetDefault(_lastSelectedId);
            var entry = _library.GetById(_lastSelectedId);
            StatusLabel.Text = $"Default set to \"{entry?.Name}\"";
            RefreshGrid();
        }

        private void OnClearDefault(object sender, RoutedEventArgs e)
        {
            _library.SetDefault(null);
            StatusLabel.Text = "Default cleared";
            RefreshGrid();
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
            _lastSelectedId = null;
            StatusLabel.Text = $"Removed {removed} item(s)";
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

            StatusLabel.Text = $"Regenerated {generated} thumbnail(s)";
            RegenThumbBtn.IsEnabled = true;
            RefreshGrid();
        }

        private async void OnDownloadMpcFill(object sender, RoutedEventArgs e)
        {
            DownloadBtn.IsEnabled = false;
            StatusLabel.Text = "Fetching card back list from MPCFill...";

            try
            {
                var (cardbacks, error) = await _mpcFill.SearchCardbacksAsync(500);
                if (error != null || cardbacks.Count == 0)
                {
                    StatusLabel.Text = error ?? "No card backs found.";
                    DownloadBtn.IsEnabled = true;
                    return;
                }

                StatusLabel.Text = $"Downloading {cardbacks.Count} card backs...";
                var results = await _mpcFill.DownloadAndCacheImagesAsync(
                    cardbacks,
                    maxConcurrency: 8,
                    onProgress: (done, total, name) =>
                        Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Downloading {done}/{total}: {name}..."));

                int added = 0, skipped = 0;
                _library.BeginBatch();
                try
                {
                    foreach (var (cb, cached) in results)
                    {
                        if (cached == null) { skipped++; continue; }
                        string displayName = $"{cb.Name} [{cb.Source}]";
                        var entry = _library.AddFromFile(cached, displayName, cb.Source);
                        if (entry != null) added++;
                        else skipped++;
                    }
                }
                finally { _library.EndBatch(); }

                StatusLabel.Text = $"Added {added} card back(s) to library ({skipped} skipped)";
                PopulateSourceFilter();
                RefreshGrid();
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error: {ex.Message}";
            }
            finally
            {
                DownloadBtn.IsEnabled = true;
            }
        }

        // ================================================================
        //  LIBRARY MANAGEMENT
        // ================================================================

        private async void OnMoveLibrary(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select new location for the back art library"
            };
            if (dialog.ShowDialog() != true) return;

            string newDir = Path.Combine(dialog.FolderName, "BackArtLibrary");
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
                _appSettings.Settings.BackArtLibraryPath = newDir;
                _appSettings.Save();
            }

            StatusLabel.Text = $"Library moved to {newDir}";
            RefreshGrid();
        }

        private async void OnExportZip(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "ZIP Archive (*.zip)|*.zip",
                Title = "Export Back Art Library",
                FileName = "BackArtLibrary.zip"
            };
            if (dialog.ShowDialog() != true) return;

            StatusLabel.Text = "Exporting library...";

            await Task.Run(() => _library.ExportToZip(dialog.FileName,
                onProgress: (done, total) =>
                    Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Compressing {done}/{total}...")));

            StatusLabel.Text = $"Exported to {Path.GetFileName(dialog.FileName)}";
        }

        private async void OnImportZip(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "ZIP Archive (*.zip)|*.zip",
                Title = "Import Back Art Library from ZIP"
            };
            if (dialog.ShowDialog() != true) return;

            StatusLabel.Text = "Importing from ZIP...";

            int added = await Task.Run(() => _library.ImportFromZip(dialog.FileName,
                onProgress: (done, total) =>
                    Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Importing {done}/{total}...")));

            StatusLabel.Text = $"Imported {added} new image(s) from ZIP";
            PopulateSourceFilter();
            RefreshGrid();
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
