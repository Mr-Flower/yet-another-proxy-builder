using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Services
{
    public class AppSettings
    {
        [JsonProperty("defaultTokenText")]
        public string DefaultTokenText { get; set; } = "TOKEN";

        [JsonProperty("defaultBleedMm")]
        public float DefaultBleedMm { get; set; } = 1.5f;

        [JsonProperty("defaultCardSizePreset")]
        public string DefaultCardSizePreset { get; set; } = "Magic: The Gathering";

        [JsonProperty("defaultPagePreset")]
        public string DefaultPagePreset { get; set; } = "A4";

        [JsonProperty("checkForUpdates")]
        public bool CheckForUpdates { get; set; } = true;
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
