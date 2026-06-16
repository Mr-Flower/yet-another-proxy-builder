using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Models
{
    /// <summary>
    /// Parameters for non-destructive image adjustments applied to card artwork.
    /// All sliders are neutral at their defaults, so a freshly-constructed instance
    /// is a no-op. Used by ImageAdjustmentProcessor (the SkiaSharp pixel work) and
    /// persisted by ImageAdjustmentStore.
    ///
    /// This type is fork-specific (not present upstream): keep it self-contained so an
    /// upstream sync can never conflict with it.
    /// </summary>
    public class ImageAdjustmentSettings
    {
        /// <summary>Master switch. When false the processor returns the source unchanged.</summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>When true, these settings are auto-applied to images imported from Scryfall.</summary>
        [JsonProperty("autoApplyToScryfall")]
        public bool AutoApplyToScryfall { get; set; }

        /// <summary>
        /// Force the card's dark border to pure black. Implemented as an edge flood-fill (see
        /// ImageAdjustmentProcessor): only the dark region connected to the image edges is blackened,
        /// so grey/grainy scan borders go to #000 while the artwork's own dark areas are left alone.
        /// </summary>
        [JsonProperty("blackenBorder")]
        public bool BlackenBorder { get; set; }

        /// <summary>
        /// 0..255 luminance threshold. An edge-connected pixel at or below this brightness counts as
        /// "border" and is flooded to black; the flood stops at the lighter card frame/art. Higher =
        /// blackens greyer borders, but risks creeping into the card.
        /// </summary>
        [JsonProperty("borderThreshold")]
        public int BorderThreshold { get; set; } = 64;

        /// <summary>True when applying this would visibly change nothing.</summary>
        [JsonIgnore]
        public bool IsNoOp => !Enabled || !BlackenBorder;

        public ImageAdjustmentSettings Clone() => new()
        {
            Enabled = Enabled,
            AutoApplyToScryfall = AutoApplyToScryfall,
            BlackenBorder = BlackenBorder,
            BorderThreshold = BorderThreshold
        };

        /// <summary>Stable short signature used to name cached adjusted files.</summary>
        public string Signature() => $"bb{(BlackenBorder ? 1 : 0)}_t{BorderThreshold}";
    }
}
