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

        /// <summary>-100..100, 0 = unchanged. Additive luminance offset.</summary>
        [JsonProperty("brightness")]
        public int Brightness { get; set; }

        /// <summary>-100..100, 0 = unchanged.</summary>
        [JsonProperty("contrast")]
        public int Contrast { get; set; }

        /// <summary>-100..100, 0 = unchanged. -100 = grayscale, +100 = doubled saturation.</summary>
        [JsonProperty("saturation")]
        public int Saturation { get; set; }

        /// <summary>
        /// 0..255, 0 = unchanged. The "black point": every channel value at or below this
        /// threshold is pushed to pure black and the remaining range is stretched back up.
        /// This is what turns Scryfall's dark-grey scan borders into absolute black.
        /// </summary>
        [JsonProperty("blackPoint")]
        public int BlackPoint { get; set; }

        /// <summary>True when applying this would visibly change nothing.</summary>
        [JsonIgnore]
        public bool IsNoOp =>
            !Enabled || (Brightness == 0 && Contrast == 0 && Saturation == 0 && BlackPoint == 0);

        public ImageAdjustmentSettings Clone() => new()
        {
            Enabled = Enabled,
            AutoApplyToScryfall = AutoApplyToScryfall,
            Brightness = Brightness,
            Contrast = Contrast,
            Saturation = Saturation,
            BlackPoint = BlackPoint
        };

        /// <summary>Stable short signature used to name cached adjusted files.</summary>
        public string Signature() =>
            $"b{Brightness}_c{Contrast}_s{Saturation}_k{BlackPoint}";
    }
}
