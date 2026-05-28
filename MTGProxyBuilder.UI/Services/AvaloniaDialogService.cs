using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.Dialogs;
using MTGProxyBuilder.UI.ViewModels;

namespace MTGProxyBuilder.UI.Services;

public class AvaloniaDialogService : IDialogService
{
    // ================================================================
    //  HELPERS
    // ================================================================

    private static Window? GetMainWindow()
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    private static IStorageProvider? GetStorage()
        => GetMainWindow() is { } w ? TopLevel.GetTopLevel(w)?.StorageProvider : null;

    /// <summary>Parses WPF-style "Name|*.ext1;*.ext2|All|*.*" filter strings.</summary>
    private static IReadOnlyList<FilePickerFileType>? ParseFilter(string? filter)
    {
        if (string.IsNullOrEmpty(filter)) return null;
        var parts = filter.Split('|');
        var types = new List<FilePickerFileType>();
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            var name = parts[i];
            var patterns = parts[i + 1].Split(';').Select(p => p.Trim()).ToArray();
            types.Add(new FilePickerFileType(name) { Patterns = patterns });
        }
        return types.Count > 0 ? types : null;
    }

    // ================================================================
    //  FILE PICKERS
    // ================================================================

    public async Task<string?> PickOpenFileAsync(string title, string filter)
    {
        var storage = GetStorage();
        if (storage == null) return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = ParseFilter(filter)
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string[]> PickOpenFilesAsync(string title, string filter)
    {
        var storage = GetStorage();
        if (storage == null) return Array.Empty<string>();

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = ParseFilter(filter)
        });

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(p => p != null)
            .Select(p => p!)
            .ToArray();
    }

    public async Task<string?> PickSaveFileAsync(string title, string filter, string defaultFileName = "")
    {
        var storage = GetStorage();
        if (storage == null) return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            FileTypeChoices = ParseFilter(filter),
            SuggestedFileName = defaultFileName
        });

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var storage = GetStorage();
        if (storage == null) return null;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    // ================================================================
    //  MESSAGE BOXES
    // ================================================================

    public Task ShowInfoAsync(string message, string title = "Information")
        => ShowMessageAsync(message, title, "#0078D4", hasCancel: false, hasNo: false);

    public Task ShowErrorAsync(string message, string title = "Error")
        => ShowMessageAsync(message, title, "#C42B1C", hasCancel: false, hasNo: false);

    public Task ShowWarningAsync(string message, string title = "Warning")
        => ShowMessageAsync(message, title, "#9D5D00", hasCancel: false, hasNo: false);

    public async Task<bool> ConfirmAsync(string message, string title = "Confirm")
    {
        var result = await ShowMessageAsync(message, title, "#0078D4", hasCancel: false, hasNo: true);
        return result == MessageResult.Yes;
    }

    public async Task<MessageResult> ConfirmCancelAsync(string message, string title = "Confirm")
        => await ShowMessageAsync(message, title, "#0078D4", hasCancel: true, hasNo: true);

    private static async Task<MessageResult> ShowMessageAsync(
        string message, string title, string accentHex, bool hasCancel, bool hasNo)
    {
        var owner = GetMainWindow();
        var result = MessageResult.Yes;

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = hasNo ? 165 : 140,
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false
        };

        var accent = Color.Parse(accentHex);
        var accentBrush = new SolidColorBrush(accent);

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var msgBlock = new TextBlock
        {
            Text = message,
            Foreground = Brushes.White,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(msgBlock, 0);
        root.Children.Add(msgBlock);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);

        Button MakeBtn(string text, bool isAccent)
        {
            return new Button
            {
                Content = text,
                Padding = new Thickness(16, 6),
                Background = isAccent ? accentBrush : new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
        }

        var okBtn = MakeBtn(hasNo ? "Yes" : "OK", isAccent: true);
        okBtn.Click += (_, _) => { result = MessageResult.Yes; dialog.Close(); };
        buttons.Children.Add(okBtn);

        if (hasNo)
        {
            var noBtn = MakeBtn("No", isAccent: false);
            noBtn.Click += (_, _) => { result = MessageResult.No; dialog.Close(); };
            buttons.Children.Add(noBtn);
        }

        if (hasCancel)
        {
            var cancelBtn = MakeBtn("Cancel", isAccent: false);
            cancelBtn.Click += (_, _) => { result = MessageResult.Cancel; dialog.Close(); };
            buttons.Children.Add(cancelBtn);
        }

        dialog.Content = root;

        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();

        return result;
    }

    // ================================================================
    //  DOMAIN DIALOGS
    // ================================================================

    public async Task ShowCardEditorAsync()
    {
        var owner = GetMainWindow();
        if (owner == null) return;

        var vm = new CardEditorViewModel(this);
        var window = new CardEditorWindow(vm);
        await window.ShowDialog(owner);
    }

    public async Task<ArtSelectorResult?> ShowArtSelectorAsync(
        CardModel card, ArtSelectorMode mode,
        ScryfallService scryfall, MpcFillService mpcFill,
        ImageCacheService imageCache, BackArtLibraryService backLibrary,
        IReadOnlyList<CardModel> allCards, object[][]? sources,
        MpcFillSearchOptions searchOptions, FrontArtLibraryService frontLibrary)
    {
        var owner = GetMainWindow();
        if (owner == null) return null;

        var dialog = new ArtSelectorWindow(
            card, mode, scryfall, mpcFill, imageCache, backLibrary,
            allCards, sources, searchOptions, frontLibrary);
        await dialog.ShowDialog(owner);

        if (dialog.ResultPath == null) return null;
        return new ArtSelectorResult(dialog.ResultPath, dialog.ApplyToSameName, dialog.ApplyToNoBack);
    }

    public async Task ShowSettingsAsync(AppSettingsService settings, MpcFillSourceManager mpcSources, MpcFillService mpcFill)
    {
        var owner = GetMainWindow();
        if (owner == null) return;

        var dialog = new SettingsWindow(settings, mpcSources, mpcFill, this);
        await dialog.ShowDialog(owner);
    }

    public async Task ShowMpcSourceManagerAsync(MpcFillSourceManager manager, MpcFillService service)
    {
        var owner = GetMainWindow();
        if (owner == null) return;

        var dialog = new MpcSourceManagerWindow(manager, service);
        await dialog.ShowDialog(owner);
    }

    public async Task ShowBackArtLibraryAsync(BackArtLibraryService library, MpcFillService mpcFill, AppSettingsService settings)
    {
        var owner = GetMainWindow();
        if (owner == null) return;

        var dialog = new BackArtLibraryWindow(library, mpcFill, settings, this);
        await dialog.ShowDialog(owner);
    }

    public async Task ShowFrontArtLibraryAsync(FrontArtLibraryService library, ImageCacheService imageCache, AppSettingsService settings, ScryfallService scryfall)
    {
        var owner = GetMainWindow();
        if (owner == null) return;

        var dialog = new FrontArtLibraryWindow(library, imageCache, settings, scryfall, this);
        await dialog.ShowDialog(owner);
    }

    public void Shutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            lifetime.Shutdown();
    }
}
