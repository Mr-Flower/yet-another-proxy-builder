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

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class FrontArtLibraryDialog : Window
    {
        private readonly FrontArtLibraryService _library;
        private readonly ImageCacheService? _imageCache;
        private string? _selectedEntryId;

        public FrontArtLibraryDialog(FrontArtLibraryService library, ImageCacheService? imageCache = null)
        {
            InitializeComponent();
            _library = library;
            _imageCache = imageCache;
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
                .OrderBy(s => s)
                .ToList();

            SourceFilter.Items.Clear();
            SourceFilter.Items.Add("All Sources");
            foreach (var s in sources)
                SourceFilter.Items.Add(s);
            SourceFilter.SelectedIndex = 0;
        }

        private void OnFilterChanged(object sender, object e)
        {
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            LibraryPanel.Children.Clear();
            _selectedEntryId = null;
            RemoveBtn.IsEnabled = false;

            string nameFilter = SearchBox?.Text?.Trim() ?? "";
            string sourceFilter = SourceFilter?.SelectedItem as string ?? "All Sources";

            var entries = _library.Entries.Where(e => File.Exists(e.FilePath)).AsEnumerable();

            if (!string.IsNullOrEmpty(nameFilter))
                entries = entries.Where(e => e.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));

            if (sourceFilter != "All Sources")
                entries = entries.Where(e => e.Source.Equals(sourceFilter, StringComparison.OrdinalIgnoreCase));

            var filteredEntries = entries.ToList();

            var imageTargets = new List<(Image img, string path)>();

            foreach (var entry in filteredEntries)
            {
                var border = new Border
                {
                    Width = 100, Height = 150, Margin = new Thickness(4),
                    Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
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
                imageTargets.Add((img, entry.FilePath));
                stack.Children.Add(imgBorder);

                var lbl = new TextBlock
                {
                    Text = entry.Name,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
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
                        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
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

        private async Task LoadThumbnailsAsync(List<(Image img, string path)> targets)
        {
            const int batchSize = 20;
            for (int i = 0; i < targets.Count; i += batchSize)
            {
                var batch = targets.Skip(i).Take(batchSize).ToList();
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
            StatusLabel.Text = "";
        }

        private void SelectEntry(string entryId, Border clickedBorder)
        {
            foreach (var child in LibraryPanel.Children)
                if (child is Border b) b.BorderBrush = Brushes.Transparent;

            clickedBorder.BorderBrush = Brushes.DodgerBlue;
            _selectedEntryId = entryId;
            RemoveBtn.IsEnabled = true;

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
            StatusLabel.Text = $"Added {added} image(s)";
            PopulateSourceFilter();
            RefreshGrid();
        }

        private void OnImportFromCache(object sender, RoutedEventArgs e)
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
            _library.BeginBatch();
            try
            {
                foreach (var (key, path, name, source) in cached)
                {
                    if (!File.Exists(path)) { skipped++; continue; }
                    string displayName = !string.IsNullOrEmpty(source)
                        ? $"{name} [{source}]" : name;
                    var entry = _library.AddFromFile(path, displayName, source);
                    if (entry != null) added++;
                    else skipped++;
                }
            }
            finally { _library.EndBatch(); }

            StatusLabel.Text = $"Imported {added} image(s) ({skipped} already in library or skipped)";
            if (added > 0)
            {
                PopulateSourceFilter();
                RefreshGrid();
            }
            ImportCacheBtn.IsEnabled = true;
        }

        private void OnRemoveSelected(object sender, RoutedEventArgs e)
        {
            if (_selectedEntryId == null) return;

            var entry = _library.GetById(_selectedEntryId);
            string name = entry?.Name ?? "this item";

            var result = MessageBox.Show($"Remove \"{name}\" from the library?",
                "Remove", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            _library.Remove(_selectedEntryId);
            StatusLabel.Text = $"Removed \"{name}\"";
            RefreshGrid();
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
