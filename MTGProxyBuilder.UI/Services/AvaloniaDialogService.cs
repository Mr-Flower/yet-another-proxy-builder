using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.Services;

/// <summary>
/// Stub implementation for Phase 2. File pickers and domain dialogs are implemented
/// in Phase 6 using Avalonia StorageProvider and custom Avalonia Windows.
/// Message boxes use MsBox.Avalonia when available.
/// </summary>
public class AvaloniaDialogService : IDialogService
{
    public Task<string?> PickOpenFileAsync(string title, string filter)
        => Task.FromResult<string?>(null);

    public Task<string[]> PickOpenFilesAsync(string title, string filter)
        => Task.FromResult(System.Array.Empty<string>());

    public Task<string?> PickSaveFileAsync(string title, string filter, string defaultFileName = "")
        => Task.FromResult<string?>(null);

    public Task ShowInfoAsync(string message, string title = "Information")
        => Task.CompletedTask;

    public Task ShowErrorAsync(string message, string title = "Error")
        => Task.CompletedTask;

    public Task ShowWarningAsync(string message, string title = "Warning")
        => Task.CompletedTask;

    public Task<bool> ConfirmAsync(string message, string title = "Confirm")
        => Task.FromResult(false);

    public Task<MessageResult> ConfirmCancelAsync(string message, string title = "Confirm")
        => Task.FromResult(MessageResult.Cancel);

    public Task<ArtSelectorResult?> ShowArtSelectorAsync(
        CardModel card, ArtSelectorMode mode,
        ScryfallService scryfall, MpcFillService mpcFill,
        ImageCacheService imageCache, BackArtLibraryService backLibrary,
        IReadOnlyList<CardModel> allCards, object[][]? sources,
        MpcFillSearchOptions searchOptions, FrontArtLibraryService frontLibrary)
        => Task.FromResult<ArtSelectorResult?>(null);

    public Task ShowSettingsAsync(AppSettingsService settings, MpcFillSourceManager mpcSources, MpcFillService mpcFill)
        => Task.CompletedTask;

    public Task ShowMpcSourceManagerAsync(MpcFillSourceManager manager, MpcFillService service)
        => Task.CompletedTask;

    public Task ShowBackArtLibraryAsync(BackArtLibraryService library, MpcFillService mpcFill, AppSettingsService settings)
        => Task.CompletedTask;

    public Task ShowFrontArtLibraryAsync(FrontArtLibraryService library, ImageCacheService imageCache, AppSettingsService settings, ScryfallService scryfall)
        => Task.CompletedTask;

    public Task ShowCardEditorAsync()
        => Task.CompletedTask;

    public void Shutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            lifetime.Shutdown();
    }
}
