using System;
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

public partial class BackArtLibraryWindow : Window
{
    private readonly BackArtLibraryService _library;
    private readonly MpcFillService _mpcFill;
    private readonly AppSettingsService? _appSettings;
    private readonly AvaloniaDialogService _dialogService;
    private ThumbnailService _thumbnails;

    public BackArtLibraryWindow(BackArtLibraryService library, MpcFillService mpcFill,
        AppSettingsService? appSettings, AvaloniaDialogService dialogService)
    {
        InitializeComponent();
        _library = library;
        _mpcFill = mpcFill;
        _appSettings = appSettings;
        _dialogService = dialogService;
        _thumbnails = new ThumbnailService(library.LibraryDirectory);
        ThumbnailConverter.SetThumbnailService(_thumbnails);

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
        SearchBar.SetSources(sources, "All Contributors");
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
        DefaultBtn.IsEnabled = false;

        var defaultEntry = _library.DefaultEntryId != null ? _library.GetById(_library.DefaultEntryId) : null;
        string defaultInfo = defaultEntry != null ? $" | Default: {defaultEntry.Name}" : "";
        int totalCount = _library.Entries.Count(e => File.Exists(e.FilePath));
        string filterInfo = filteredEntries.Count < totalCount ? $" (showing {filteredEntries.Count} of {totalCount})" : "";
        CountLabel.Text = $"{totalCount} item(s) in library{filterInfo}{defaultInfo}";
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
        DefaultBtn.IsEnabled = selected.Count > 0;

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
            "Add Image to Back Art Library",
            "Image Files|*.png;*.jpg;*.jpeg;*.bmp|All Files|*.*");

        foreach (var file in files)
            _library.AddFromFile(file);
        RefreshGrid();
    }

    private void OnSetDefault(object? sender, RoutedEventArgs e)
    {
        if (LibraryListBox.SelectedItem is BackArtEntry entry)
        {
            _library.SetDefault(entry.Id);
            RefreshGrid();
        }
    }

    private void OnClearDefault(object? sender, RoutedEventArgs e)
    {
        _library.SetDefault(null);
        RefreshGrid();
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

    private async void OnDownloadMpcFill(object? sender, RoutedEventArgs e)
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
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                        () => StatusLabel.Text = $"Downloading {done}/{total}: {name}..."));

            int added = 0, skipped = 0;
            _library.BeginBatch();
            try
            {
                foreach (var (cb, cached) in results)
                {
                    if (cached == null) { skipped++; continue; }
                    string displayName = $"{cb.Name} [{cb.Source}]";
                    var entry = _library.AddFromFile(cached, displayName, cb.Source);
                    if (entry != null)
                    {
                        _library.ApplyMpcFillDefaults(entry.Id, cb.Source);
                        added++;
                    }
                    else skipped++;
                }
            }
            finally { _library.EndBatch(); }

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

    private async void OnMoveLibrary(object? sender, RoutedEventArgs e)
    {
        var folder = await _dialogService.PickFolderAsync("Select new location for back art library");
        if (folder == null) return;

        string newDir = Path.Combine(folder, "BackArtLibrary");
        if (string.Equals(newDir, _library.LibraryDirectory, StringComparison.OrdinalIgnoreCase)) return;

        if (!await _dialogService.ConfirmAsync(
            $"Move {_library.Entries.Count} image(s) to:\n{newDir}\n\nThis will move all files and delete the old location.",
            "Move Library")) return;

        StatusLabel.Text = "Moving library...";

        System.Collections.Generic.List<string>? newEntryIds = null;
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
            _appSettings.Settings.BackArtLibraryPath = newDir;
            _appSettings.Save();
        }

        RefreshGrid();
    }

    private async void OnExportZip(object? sender, RoutedEventArgs e)
    {
        var path = await _dialogService.PickSaveFileAsync(
            "Export Back Art Library", "ZIP Archive|*.zip", "BackArtLibrary.zip");
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
            "Import Back Art Library from ZIP", "ZIP Archive|*.zip");
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
