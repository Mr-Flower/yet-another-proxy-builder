using System.Collections.Generic;
using System.Threading.Tasks;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.Services;

public enum MessageResult { Yes, No, Cancel }

public enum ArtSelectorMode { Front, Back }

public record ArtSelectorResult(string ResultPath, bool ApplyToSameName, bool ApplyToNoBack);

public interface IDialogService
{
    // File pickers
    Task<string?> PickOpenFileAsync(string title, string filter);
    Task<string[]> PickOpenFilesAsync(string title, string filter);
    Task<string?> PickSaveFileAsync(string title, string filter, string defaultFileName = "");

    // Message boxes
    Task ShowInfoAsync(string message, string title = "Information");
    Task ShowErrorAsync(string message, string title = "Error");
    Task ShowWarningAsync(string message, string title = "Warning");
    Task<bool> ConfirmAsync(string message, string title = "Confirm");
    Task<MessageResult> ConfirmCancelAsync(string message, string title = "Confirm");

    // Domain dialogs (implemented in Phase 6)
    Task<ArtSelectorResult?> ShowArtSelectorAsync(
        CardModel card, ArtSelectorMode mode,
        ScryfallService scryfall, MpcFillService mpcFill,
        ImageCacheService imageCache, BackArtLibraryService backLibrary,
        IReadOnlyList<CardModel> allCards, object[][]? sources,
        MpcFillSearchOptions searchOptions, FrontArtLibraryService frontLibrary);

    Task ShowSettingsAsync(AppSettingsService settings, MpcFillSourceManager mpcSources, MpcFillService mpcFill);
    Task ShowMpcSourceManagerAsync(MpcFillSourceManager manager, MpcFillService service);
    Task ShowBackArtLibraryAsync(BackArtLibraryService library, MpcFillService mpcFill, AppSettingsService settings);
    Task ShowFrontArtLibraryAsync(FrontArtLibraryService library, ImageCacheService imageCache, AppSettingsService settings, ScryfallService scryfall);
    Task ShowCardEditorAsync();

    // Fork-specific: image adjustment editor (brightness/contrast/saturation/black-point).
    // Returns the chosen settings, or null if the user cancelled.
    Task<ImageAdjustmentSettings?> ShowImageAdjustmentAsync(string imagePath, ImageAdjustmentSettings current);

    void Shutdown();
}
