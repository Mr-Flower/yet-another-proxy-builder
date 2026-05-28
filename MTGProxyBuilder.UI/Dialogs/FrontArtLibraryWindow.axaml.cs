using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.Converters;
using MTGProxyBuilder.UI.Services;

namespace MTGProxyBuilder.UI.Dialogs;

public partial class FrontArtLibraryWindow : Window
{
    private readonly FrontArtLibraryService _library;
    private readonly ImageCacheService? _imageCache;
    private readonly AppSettingsService? _appSettings;
    private readonly ScryfallService? _scryfall;
    private readonly AvaloniaDialogService _dialogService;
    private ThumbnailService _thumbnails;

    public FrontArtLibraryWindow(FrontArtLibraryService library, ImageCacheService? imageCache,
        AppSettingsService? appSettings, ScryfallService? scryfall, AvaloniaDialogService dialogService)
    {
        InitializeComponent();
        _library = library;
        _imageCache = imageCache;
        _appSettings = appSettings;
        _scryfall = scryfall;
        _dialogService = dialogService;
        _thumbnails = new ThumbnailService(library.LibraryDirectory);
        ThumbnailConverter.SetThumbnailService(_thumbnails);
        ImportCacheBtn.IsVisible = imageCache != null;

        Loaded += (_, _) =>
        {
            PopulateSourceFilter();
            RefreshGrid();
        };
    }

    // ================================================================
    //  SEARCH & FILTER
    // ================================================================

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
        var entries = _library.Entries.Where(e => File.Exists(e.FilePath)).AsEnumerable();

        var searchPredicate = LibrarySearchParser.Parse(SearchBar.SearchText);
        entries = entries.Where(searchPredicate);

        if (!SearchBar.IsAllSourcesSelected)
        {
            string sourceFilter = SearchBar.SelectedSource;
            entries = entries.Where(e => e.Source.Equals(sourceFilter, StringComparison.OrdinalIgnoreCase));
        }

        var filteredEntries = entries.ToList();
        LibraryListBox.ItemsSource = filteredEntries;

        RemoveBtn.IsEnabled = false;
        RemoveBtn.Content = "Remove Selected";

        int totalCount = _library.Entries.Count(e => File.Exists(e.FilePath));
        string filterInfo = filteredEntries.Count < totalCount ? $" (showing {filteredEntries.Count} of {totalCount})" : "";
        CountLabel.Text = $"{totalCount} item(s) in library{filterInfo}";
        StatusLabel.Text = "";
    }

    // ================================================================
    //  SELECTION
    // ================================================================

    private void OnListBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selected = LibraryListBox.SelectedItems?.Cast<BackArtEntry>().ToList() ?? new();
        RemoveBtn.IsEnabled = selected.Count > 0;
        RemoveBtn.Content = selected.Count > 1 ? $"Remove Selected ({selected.Count})" : "Remove Selected";

        var entry = selected.LastOrDefault();
        if (entry != null && File.Exists(entry.FilePath))
        {
            string sourceInfo = !string.IsNullOrEmpty(entry.Source) && entry.Source != "Local"
                ? $"Source: {entry.Source}\n" : "";
            PreviewPanel.ShowImage(entry.FilePath, entry.Name, $"{sourceInfo}{Path.GetFileName(entry.FilePath)}");
        }
    }

    private void OnListBoxDoubleClick(object? sender, TappedEventArgs e)
    {
        if (LibraryListBox.SelectedItem is BackArtEntry entry && File.Exists(entry.FilePath))
        {
            var preview = new ImagePreviewWindow(entry.FilePath, entry.Name);
            _ = preview.ShowDialog(this);
        }
    }

    // ================================================================
    //  ACTIONS
    // ================================================================

    private async void OnAddFromFile(object? sender, RoutedEventArgs e)
    {
        var files = await _dialogService.PickOpenFilesAsync(
            "Add Image to Front Art Library",
            "Image Files|*.png;*.jpg;*.jpeg;*.bmp|All Files|*.*");

        foreach (var file in files)
            _library.AddFromFile(file);
        PopulateSourceFilter();
        RefreshGrid();
    }

    private async void OnImportFromCache(object? sender, RoutedEventArgs e)
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
                string displayName = !string.IsNullOrEmpty(source) ? $"{name} [{source}]" : name;
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

        // Populate metadata from Scryfall
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
                if (sc != null) _library.ApplyMetadata(entry.Id, sc);
            }
        }

        // Apply MPCFill defaults
        foreach (var (id, _) in newEntries)
        {
            var entry = _library.GetById(id);
            if (entry != null) _library.ApplyMpcFillDefaults(id, entry.Source);
        }

        // Generate thumbnails
        if (newEntries.Count > 0)
        {
            StatusLabel.Text = $"Generating thumbnails for {newEntries.Count} new image(s)...";
            await Task.Run(() => _thumbnails.RegenerateAll(newEntries,
                onProgress: (done, total) =>
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                        () => StatusLabel.Text = $"Generating thumbnails {done}/{total}...")));
            ThumbnailConverter.ClearCache();
        }

        foreach (var key in importedCacheKeys)
            _imageCache!.Remove(key);

        if (added > 0)
        {
            PopulateSourceFilter();
            RefreshGrid();
        }
        ImportCacheBtn.IsEnabled = true;
    }

    private async void OnRemoveSelected(object? sender, RoutedEventArgs e)
    {
        var selected = LibraryListBox.SelectedItems?.Cast<BackArtEntry>().ToList() ?? new();
        if (selected.Count == 0) return;

        string message = selected.Count == 1
            ? $"Remove \"{selected[0].Name}\" from the library?"
            : $"Remove {selected.Count} items from the library?";

        if (!await _dialogService.ConfirmAsync(message, "Remove")) return;

        foreach (var entry in selected)
        {
            _thumbnails.Delete(entry.Id);
            _library.Remove(entry.Id);
        }
        PopulateSourceFilter();
        RefreshGrid();
    }

    private async void OnRegenerateThumbnails(object? sender, RoutedEventArgs e)
    {
        RegenThumbBtn.IsEnabled = false;
        StatusLabel.Text = "Regenerating thumbnails...";

        var entries = _library.Entries
            .Where(en => File.Exists(en.FilePath))
            .Select(en => (en.Id, en.FilePath))
            .ToList();

        await Task.Run(() =>
            _thumbnails.RegenerateAll(entries,
                onProgress: (done, total) =>
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                        () => StatusLabel.Text = $"Regenerating thumbnails {done}/{total}...")));

        ThumbnailConverter.ClearCache();
        RegenThumbBtn.IsEnabled = true;
        RefreshGrid();
    }

    private async void OnMoveLibrary(object? sender, RoutedEventArgs e)
    {
        var folder = await _dialogService.PickFolderAsync("Select new location for front art library");
        if (folder == null) return;

        string newDir = Path.Combine(folder, "FrontArtLibrary");
        if (string.Equals(newDir, _library.LibraryDirectory, StringComparison.OrdinalIgnoreCase)) return;

        if (!await _dialogService.ConfirmAsync(
            $"Move {_library.Entries.Count} image(s) to:\n{newDir}\n\nThis will move all files and delete the old location.",
            "Move Library")) return;

        StatusLabel.Text = "Moving library...";

        List<string>? newEntryIds = null;
        await Task.Run(() => newEntryIds = _library.MoveToDirectory(newDir,
            onProgress: (done, total) =>
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                    () => StatusLabel.Text = $"Moving {done}/{total}...")));

        _thumbnails = new ThumbnailService(_library.LibraryDirectory);
        ThumbnailConverter.SetThumbnailService(_thumbnails);
        ThumbnailConverter.ClearCache();

        if (newEntryIds is { Count: > 0 })
        {
            var toGenerate = _library.Entries
                .Where(en => newEntryIds.Contains(en.Id) && File.Exists(en.FilePath))
                .Select(en => (en.Id, en.FilePath)).ToList();
            if (toGenerate.Count > 0)
                await Task.Run(() => _thumbnails.RegenerateAll(toGenerate));
        }

        if (_appSettings != null)
        {
            _appSettings.Settings.FrontArtLibraryPath = newDir;
            _appSettings.Save();
        }

        RefreshGrid();
    }

    private async void OnExportZip(object? sender, RoutedEventArgs e)
    {
        var path = await _dialogService.PickSaveFileAsync(
            "Export Front Art Library", "ZIP Archive|*.zip", "FrontArtLibrary.zip");
        if (path == null) return;

        StatusLabel.Text = "Exporting library...";
        await Task.Run(() => _library.ExportToZip(path,
            onProgress: (done, total) =>
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                    () => StatusLabel.Text = $"Compressing {done}/{total}...")));
        StatusLabel.Text = "";
    }

    private async void OnImportZip(object? sender, RoutedEventArgs e)
    {
        var path = await _dialogService.PickOpenFileAsync(
            "Import Front Art Library from ZIP", "ZIP Archive|*.zip");
        if (path == null) return;

        StatusLabel.Text = "Importing from ZIP...";
        await Task.Run(() => _library.ImportFromZip(path,
            onProgress: (done, total) =>
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                    () => StatusLabel.Text = $"Importing {done}/{total}...")));

        PopulateSourceFilter();
        RefreshGrid();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
