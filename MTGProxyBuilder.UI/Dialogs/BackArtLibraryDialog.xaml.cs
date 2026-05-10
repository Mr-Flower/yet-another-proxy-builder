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
    public partial class BackArtLibraryDialog : Window
    {
        private readonly BackArtLibraryService _library;
        private readonly MpcFillService _mpcFill;
        private string? _selectedEntryId;

        public BackArtLibraryDialog(BackArtLibraryService library, MpcFillService mpcFill)
        {
            InitializeComponent();
            _library = library;
            _mpcFill = mpcFill;
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

        private void OnFilterChanged(object sender, object e)
        {
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            LibraryPanel.Children.Clear();
            _selectedEntryId = null;
            RemoveBtn.IsEnabled = false;
            DefaultBtn.IsEnabled = false;

            string nameFilter = SearchBox?.Text?.Trim() ?? "";
            string sourceFilter = SourceFilter?.SelectedItem as string ?? "All Contributors";

            var entries = _library.Entries.Where(e => File.Exists(e.FilePath)).AsEnumerable();

            if (!string.IsNullOrEmpty(nameFilter))
                entries = entries.Where(e => e.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));

            if (sourceFilter != "All Contributors")
                entries = entries.Where(e => e.Source.Equals(sourceFilter, StringComparison.OrdinalIgnoreCase));

            var filteredEntries = entries.ToList();

            // Build all tiles immediately with placeholder backgrounds
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
                imageTargets.Add((img, entry.FilePath));
                stack.Children.Add(imgBorder);

                bool isDefault = _library.IsDefault(entry.Id);

                var lbl = new TextBlock
                {
                    Text = isDefault ? "\u2605 " + entry.Name : entry.Name,
                    Foreground = isDefault
                        ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                        : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
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
                        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                        FontSize = 8, TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(3, 0, 3, 2)
                    };
                    stack.Children.Add(srcLbl);
                }

                if (isDefault)
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));

                border.Child = stack;

                string entryId = entry.Id;
                border.MouseLeftButtonUp += (_, _) => SelectEntry(entryId, border);

                LibraryPanel.Children.Add(border);
            }

            var defaultEntry = _library.DefaultEntryId != null ? _library.GetById(_library.DefaultEntryId) : null;
            string defaultInfo = defaultEntry != null ? $" | Default: {defaultEntry.Name}" : "";
            int totalCount = _library.Entries.Count(e => File.Exists(e.FilePath));
            string filterInfo = filteredEntries.Count < totalCount ? $" (showing {filteredEntries.Count} of {totalCount})" : "";
            CountLabel.Text = $"{totalCount} item(s) in library{filterInfo}{defaultInfo}";
            StatusLabel.Text = "Loading thumbnails...";

            // Load thumbnails on background thread in batches
            _ = LoadThumbnailsAsync(imageTargets);
        }

        private async Task LoadThumbnailsAsync(List<(Image img, string path)> targets)
        {
            const int batchSize = 20;
            for (int i = 0; i < targets.Count; i += batchSize)
            {
                var batch = targets.Skip(i).Take(batchSize).ToList();

                // Load bitmaps on background thread
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

                // Assign to UI on dispatcher
                for (int j = 0; j < batch.Count && j < bitmaps.Count; j++)
                {
                    if (bitmaps[j] != null)
                        batch[j].img.Source = bitmaps[j];
                }
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
            DefaultBtn.IsEnabled = true;
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
            if (_selectedEntryId == null) return;
            _library.SetDefault(_selectedEntryId);
            var entry = _library.GetById(_selectedEntryId);
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

                int added = 0, skipped = 0;
                for (int i = 0; i < cardbacks.Count; i++)
                {
                    var cb = cardbacks[i];
                    StatusLabel.Text = $"Downloading {i + 1}/{cardbacks.Count}: {cb.Name}...";
                    await Task.Delay(5);

                    var cached = await _mpcFill.DownloadAndCacheImageAsync(cb);
                    if (cached == null) { skipped++; continue; }

                    string displayName = $"{cb.Name} [{cb.Source}]";
                    var entry = _library.AddFromFile(cached, displayName, cb.Source);
                    if (entry != null) added++;
                    else skipped++;

                    await Task.Delay(20);
                }

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

        private void OnClose(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
