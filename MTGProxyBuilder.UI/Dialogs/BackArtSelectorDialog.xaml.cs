using System;
using System.Collections.Generic;
using System.IO;
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
    public partial class BackArtSelectorDialog : Window
    {
        private readonly BackArtLibraryService _library;
        private readonly CardModel _card;

        /// <summary>The selected back art path, or null if cleared, or empty if cancelled.</summary>
        public string? ResultPath { get; private set; }
        public bool WasCleared { get; private set; }

        // Tile tracking for search/filter
        private record TileInfo(Border Tile, string Name, string Source, bool IsAction = false);
        private readonly List<TileInfo> _allTiles = new();

        public BackArtSelectorDialog(CardModel card, BackArtLibraryService library)
        {
            InitializeComponent();
            _card = card;
            _library = library;
            CardNameLabel.Text = $"for: {card.Name}";
            BuildOptions();
        }

        private void BuildOptions()
        {
            OptionsPanel.Children.Clear();
            _allTiles.Clear();

            // Track which paths we've already shown to avoid duplicates
            var shownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Original Scryfall back (from when the card was first imported)
            if (!string.IsNullOrEmpty(_card.OriginalBackArtworkPath) && File.Exists(_card.OriginalBackArtworkPath))
            {
                bool isCurrent = string.Equals(_card.BackArtworkPath, _card.OriginalBackArtworkPath, StringComparison.OrdinalIgnoreCase);
                AddOption("Original (Scryfall)", _card.OriginalBackArtworkPath, isCurrent, "Scryfall");
                shownPaths.Add(_card.OriginalBackArtworkPath);
            }

            // 2. Current back (if different from original and from library)
            if (!string.IsNullOrEmpty(_card.BackArtworkPath) && File.Exists(_card.BackArtworkPath)
                && !shownPaths.Contains(_card.BackArtworkPath))
            {
                AddOption("Current Back", _card.BackArtworkPath, true, "Current");
                shownPaths.Add(_card.BackArtworkPath);
            }

            // 3. Library entries
            foreach (var entry in _library.Entries)
            {
                if (File.Exists(entry.FilePath) && !shownPaths.Contains(entry.FilePath))
                {
                    AddOption(entry.Name, entry.FilePath, false, entry.Source);
                    shownPaths.Add(entry.FilePath);
                }
            }

            // 4. Actions
            AddActionTile("+ Add to Library", OnAddToLibrary);
            AddActionTile("Browse File...", OnBrowseFile);

            PopulateSourceFilter();
        }

        private void AddOption(string label, string imagePath, bool isCurrent, string source = "")
        {
            var border = Controls.ArtTileBuilder.CreateOptionTile(label, imagePath, isCurrent, label, decodePixelWidth: 200);

            string path = imagePath;
            border.MouseLeftButtonUp += (_, _) => SelectOption(label, path, border);

            OptionsPanel.Children.Add(border);
            _allTiles.Add(new TileInfo(border, label, source));
        }

        private void AddActionTile(string label, Action action)
        {
            var border = Controls.ArtTileBuilder.CreateActionTile(label);
            border.MouseLeftButtonUp += (_, _) => action();

            OptionsPanel.Children.Add(border);
            _allTiles.Add(new TileInfo(border, label, "", IsAction: true));
        }

        private void SelectOption(string label, string path, Border selectedBorder)
        {
            // Clear all borders
            foreach (var child in OptionsPanel.Children)
            {
                if (child is Border b)
                    b.BorderBrush = Brushes.Transparent;
            }
            selectedBorder.BorderBrush = Brushes.DodgerBlue;

            ResultPath = path;
            SelectedLabel.Text = label;
            SelectedPath.Text = path;
            OkBtn.IsEnabled = true;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 100;
                bmp.EndInit();
                bmp.Freeze();
                PreviewImage.Source = bmp;
            }
            catch { PreviewImage.Source = null; }
        }

        private void OnAddToLibrary()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Add Image to Back Art Library"
            };
            if (dialog.ShowDialog() != true) return;

            var entry = _library.AddFromFile(dialog.FileName);
            if (entry != null)
            {
                BuildOptions(); // Rebuild to show new entry
            }
        }

        private void OnBrowseFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Select Back Artwork"
            };
            if (dialog.ShowDialog() != true) return;

            ResultPath = dialog.FileName;
            SelectedLabel.Text = Path.GetFileName(dialog.FileName);
            SelectedPath.Text = dialog.FileName;
            OkBtn.IsEnabled = true;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(dialog.FileName, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 100;
                bmp.EndInit();
                bmp.Freeze();
                PreviewImage.Source = bmp;
            }
            catch { PreviewImage.Source = null; }
        }

        // ================================================================
        //  SEARCH & SOURCE FILTER
        // ================================================================

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchPlaceholder != null)
                SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilters();
        }

        private void OnSourceFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            if (_allTiles.Count == 0) return;

            string searchText = SearchBox?.Text?.Trim() ?? "";
            string sourceFilter = (SourceFilterBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";

            foreach (var tile in _allTiles)
            {
                if (tile.IsAction)
                {
                    tile.Tile.Visibility = Visibility.Visible;
                    continue;
                }

                bool matchesSearch = string.IsNullOrEmpty(searchText) ||
                    tile.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    tile.Source.Contains(searchText, StringComparison.OrdinalIgnoreCase);

                bool matchesSource = string.IsNullOrEmpty(sourceFilter) ||
                    tile.Source.Equals(sourceFilter, StringComparison.OrdinalIgnoreCase);

                tile.Tile.Visibility = matchesSearch && matchesSource
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void PopulateSourceFilter()
        {
            var currentSource = (SourceFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            SourceFilterBox.SelectionChanged -= OnSourceFilterChanged;

            SourceFilterBox.Items.Clear();
            var allItem = new ComboBoxItem { Content = "All Sources", Tag = "" };
            SourceFilterBox.Items.Add(allItem);
            SourceFilterBox.SelectedItem = allItem;

            var sources = _allTiles
                .Where(t => !t.IsAction && !string.IsNullOrEmpty(t.Source))
                .Select(t => t.Source)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();

            foreach (var source in sources)
            {
                var item = new ComboBoxItem { Content = source, Tag = source };
                SourceFilterBox.Items.Add(item);
                if (source.Equals(currentSource, StringComparison.OrdinalIgnoreCase))
                    SourceFilterBox.SelectedItem = item;
            }

            SourceFilterBox.SelectionChanged += OnSourceFilterChanged;
        }

        private void OkClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void ClearClick(object sender, RoutedEventArgs e)
        {
            WasCleared = true;
            ResultPath = null;
            DialogResult = true;
        }
    }
}
