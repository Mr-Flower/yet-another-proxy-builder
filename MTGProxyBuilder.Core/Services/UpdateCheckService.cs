using Newtonsoft.Json.Linq;

namespace MTGProxyBuilder.Core.Services
{
    public class UpdateInfo
    {
        public string LatestVersion { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public bool IsUpdateAvailable { get; set; }
    }

    public class UpdateCheckService
    {
        private const string GitHubRepo = "Mr-Flower/yet-another-proxy-builder";
        private readonly HttpClient _httpClient;

        public UpdateCheckService()
        {
            _httpClient = SharedHttp.CreateClient();
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        }

        /// <summary>
        /// Checks GitHub releases for a newer version.
        /// Returns null on any failure (network, parse, etc.) — never blocks the app.
        /// </summary>
        public async Task<UpdateInfo?> CheckForUpdateAsync(string currentVersion)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"https://api.github.com/repos/{GitHubRepo}/releases/latest");

                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var release = JObject.Parse(json);

                string tagName = release["tag_name"]?.ToString() ?? "";
                string latestVersion = tagName.TrimStart('v');
                string downloadUrl = release["html_url"]?.ToString() ?? "";
                string body = release["body"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(latestVersion)) return null;

                bool isNewer = IsNewerVersion(currentVersion, latestVersion);

                return new UpdateInfo
                {
                    CurrentVersion = currentVersion,
                    LatestVersion = latestVersion,
                    DownloadUrl = downloadUrl,
                    ReleaseNotes = body.Length > 200 ? body[..200] + "..." : body,
                    IsUpdateAvailable = isNewer
                };
            }
            catch
            {
                return null; // never crash the app over an update check
            }
        }

        private static bool IsNewerVersion(string current, string latest)
        {
            try
            {
                // Strip pre-release suffixes (e.g. "0.0.0-dev" → "0.0.0")
                string currentClean = current.Split('-')[0];
                string latestClean = latest.Split('-')[0];

                // Dev builds (0.0.0) always show update if any release exists
                if (currentClean == "0.0.0" && latestClean != "0.0.0") return true;

                var currentParts = currentClean.Split('.').Select(int.Parse).ToArray();
                var latestParts = latestClean.Split('.').Select(int.Parse).ToArray();

                for (int i = 0; i < Math.Max(currentParts.Length, latestParts.Length); i++)
                {
                    int c = i < currentParts.Length ? currentParts[i] : 0;
                    int l = i < latestParts.Length ? latestParts[i] : 0;
                    if (l > c) return true;
                    if (l < c) return false;
                }
                return false;
            }
            catch { return false; }
        }
    }
}
