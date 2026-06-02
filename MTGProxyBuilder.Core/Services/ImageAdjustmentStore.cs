using MTGProxyBuilder.Core.Models;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// Persists image-adjustment state to its own JSON file
    /// (~/.config/MTGProxyBuilder/image_adjustments.json), independent of the upstream
    /// AppSettings model so an upstream sync can never conflict with it.
    ///
    /// Stores:
    ///  - the global default settings (used for Scryfall auto-apply),
    ///  - a map adjusted-file -> original-file, so re-opening the dialog can reload the
    ///    untouched original,
    ///  - the last settings used per original file, so sliders restore their positions.
    ///
    /// Each instance loads on construction; mutating calls save immediately. Callers
    /// construct a fresh instance when they need the latest on-disk state.
    /// </summary>
    public class ImageAdjustmentStore
    {
        private readonly string _path;
        private StoreData _data;

        public ImageAdjustmentStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "YetAnotherProxyBuilder");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "image_adjustments.json");
            _data = Load();
        }

        public ImageAdjustmentSettings Default => _data.Default.Clone();

        public void SaveDefault(ImageAdjustmentSettings settings)
        {
            _data.Default = settings.Clone();
            Save();
        }

        /// <summary>
        /// If the given path is a known adjusted file, returns the original it was derived
        /// from; otherwise returns the path unchanged (it is itself an original).
        /// </summary>
        public string ResolveOriginal(string? path)
        {
            if (string.IsNullOrEmpty(path)) return path ?? string.Empty;
            return _data.AdjustedToOriginal.TryGetValue(path, out var original) ? original : path;
        }

        /// <summary>Last settings used for an original image, or the global default.</summary>
        public ImageAdjustmentSettings GetSettingsFor(string originalPath)
        {
            if (!string.IsNullOrEmpty(originalPath)
                && _data.OriginalToSettings.TryGetValue(originalPath, out var s))
                return s.Clone();
            return _data.Default.Clone();
        }

        /// <summary>Records the mapping produced by baking an adjusted file from an original.</summary>
        public void Record(string originalPath, string adjustedPath, ImageAdjustmentSettings settings)
        {
            if (string.IsNullOrEmpty(originalPath)) return;
            _data.OriginalToSettings[originalPath] = settings.Clone();
            if (!string.IsNullOrEmpty(adjustedPath) && adjustedPath != originalPath)
                _data.AdjustedToOriginal[adjustedPath] = originalPath;
            Save();
        }

        private StoreData Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    var data = JsonConvert.DeserializeObject<StoreData>(json);
                    if (data != null)
                    {
                        data.Default ??= new ImageAdjustmentSettings { Enabled = false };
                        data.AdjustedToOriginal ??= new();
                        data.OriginalToSettings ??= new();
                        return data;
                    }
                }
            }
            catch { }
            // First run: a disabled default so nothing is altered until the user opts in.
            return new StoreData { Default = new ImageAdjustmentSettings { Enabled = false } };
        }

        private void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_data, Formatting.Indented);
                File.WriteAllText(_path, json);
            }
            catch { }
        }

        private class StoreData
        {
            [JsonProperty("default")]
            public ImageAdjustmentSettings Default { get; set; } = new() { Enabled = false };

            [JsonProperty("adjustedToOriginal")]
            public Dictionary<string, string> AdjustedToOriginal { get; set; } = new();

            [JsonProperty("originalToSettings")]
            public Dictionary<string, ImageAdjustmentSettings> OriginalToSettings { get; set; } = new();
        }
    }
}
