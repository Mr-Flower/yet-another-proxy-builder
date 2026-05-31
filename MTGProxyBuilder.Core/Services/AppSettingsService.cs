using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Services
{
    public class AppSettings
    {
        [JsonProperty("defaultTokenText")]
        public string DefaultTokenText { get; set; } = "TOKEN";

        // New projects start at the full MakePlayingCards 1/8" bleed; reduce it per-project from the
        // Layout panel. (No longer surfaced in General settings — it is the fixed max, only trimmed.)
        [JsonProperty("defaultBleedMm")]
        public float DefaultBleedMm { get; set; } = Constants.MpcBleedMm;

        [JsonProperty("defaultCardSizePreset")]
        public string DefaultCardSizePreset { get; set; } = "Magic: The Gathering";

        [JsonProperty("defaultPagePreset")]
        public string DefaultPagePreset { get; set; } = "A4";

        [JsonProperty("checkForUpdates")]
        public bool CheckForUpdates { get; set; } = true;

        /// <summary>When enabled, every downloaded card image (Scryfall + MPCFill) is also copied,
        /// at original size, into the front art library so it persists and is reusable by name.</summary>
        [JsonProperty("autoSaveDownloadedToLibrary")]
        public bool AutoSaveDownloadedToLibrary { get; set; }

        [JsonProperty("mpcFillUseFavoritesOnly")]
        public bool MpcFillUseFavoritesOnly { get; set; }

        [JsonProperty("mpcFillDefaultMinDpi")]
        public int MpcFillDefaultMinDpi { get; set; }

        [JsonProperty("mpcFillDefaultMaxDpi")]
        public int MpcFillDefaultMaxDpi { get; set; } = 1500;

        [JsonProperty("mpcFillDefaultFuzzySearch")]
        public bool MpcFillDefaultFuzzySearch { get; set; } = true;

        [JsonProperty("mpcFillDefaultSortBy")]
        public string MpcFillDefaultSortBy { get; set; } = "nameAscending";

        [JsonProperty("mpcFillCardTypes")]
        public List<string> MpcFillCardTypes { get; set; } = new() { "CARD", "TOKEN" };

        [JsonProperty("mpcFillFilterCardbacks")]
        public bool MpcFillFilterCardbacks { get; set; }

        [JsonProperty("mpcFillMaximumSize")]
        public int MpcFillMaximumSize { get; set; } = 30;

        [JsonProperty("mpcFillLanguages")]
        public List<string> MpcFillLanguages { get; set; } = new();

        [JsonProperty("mpcFillExcludeNsfw")]
        public bool MpcFillExcludeNsfw { get; set; }

        [JsonProperty("mpcFillExcludeAiArt")]
        public bool MpcFillExcludeAiArt { get; set; }

        [JsonProperty("mpcFillExcludeTags")]
        public List<string> MpcFillExcludeTags { get; set; } = new();

        [JsonProperty("mpcFillIncludeTags")]
        public List<string> MpcFillIncludeTags { get; set; } = new();

        [JsonProperty("frontArtLibraryPath")]
        public string? FrontArtLibraryPath { get; set; }

        [JsonProperty("backArtLibraryPath")]
        public string? BackArtLibraryPath { get; set; }
    }

    public class AppSettingsService
    {
        private readonly string _settingsPath;
        private AppSettings _settings;

        public AppSettingsService()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder");
            Directory.CreateDirectory(dir);
            _settingsPath = Path.Combine(dir, "app_settings.json");
            _settings = Load();
        }

        public AppSettings Settings => _settings;

        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText(_settingsPath, json);
            }
            catch { }
        }

        private AppSettings Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    return JsonConvert.DeserializeObject<AppSettings>(json) ?? new();
                }
            }
            catch { }
            return new AppSettings();
        }
    }
}
