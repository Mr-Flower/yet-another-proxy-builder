using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.Services;

/// <summary>
/// Thin UI-layer facade over ImageAdjustmentProcessor + ImageAdjustmentStore.
/// Exists so the only edits to the (upstream-churned) view models are a couple of
/// one-line calls — all real logic lives in these fork-specific files.
///
/// Baking repoints the card's ArtworkPath to an adjusted file; because the on-screen
/// preview and the PDF generator both just follow ArtworkPath, no render code needs to
/// change. The untouched original is always recoverable via the store.
/// </summary>
public class ImageAdjustmentService
{
    private readonly ImageAdjustmentProcessor _processor = new();

    /// <summary>A fresh store reflecting the latest on-disk state.</summary>
    public ImageAdjustmentStore OpenStore() => new();

    /// <summary>
    /// Applies <paramref name="settings"/> to the card's front artwork and repoints it
    /// to the baked file. Passing no-op settings reverts the card to its original image.
    /// Returns the resulting path.
    /// </summary>
    public string BakeFront(CardModel card, ImageAdjustmentSettings settings)
    {
        var store = OpenStore();
        string original = store.ResolveOriginal(card.ArtworkPath);

        if (settings.IsNoOp)
        {
            card.ArtworkPath = original;
            store.Record(original, original, settings);
            return original;
        }

        string result = _processor.GetAdjustedImage(original, settings) ?? original;
        store.Record(original, result, settings);
        card.ArtworkPath = result;
        // The adjustment is baked into this (cached-resolution) image; drop the full-res URL so the
        // PDF export uses the baked result instead of re-downloading the un-adjusted original.
        card.FullResFrontUrl = null;
        return result;
    }

    /// <summary>
    /// Auto-applies the saved global default to a freshly-imported card (front and back),
    /// but only when the user enabled "apply automatically to Scryfall imports".
    /// No-op otherwise. Safe to call on any card.
    /// </summary>
    public void AutoApply(CardModel card)
    {
        var store = OpenStore();
        var def = store.Default;
        if (def.IsNoOp || !def.AutoApplyToScryfall)
            return;

        // Front
        string frontOriginal = store.ResolveOriginal(card.ArtworkPath);
        if (!string.IsNullOrEmpty(frontOriginal))
        {
            string adjusted = _processor.GetAdjustedImage(frontOriginal, def) ?? frontOriginal;
            store.Record(frontOriginal, adjusted, def);
            card.ArtworkPath = adjusted;
        }

        // Back (if any)
        if (!string.IsNullOrEmpty(card.BackArtworkPath))
        {
            string backOriginal = store.ResolveOriginal(card.BackArtworkPath);
            string adjustedBack = _processor.GetAdjustedImage(backOriginal, def) ?? backOriginal;
            store.Record(backOriginal, adjustedBack, def);
            card.BackArtworkPath = adjustedBack;
        }
    }
}
