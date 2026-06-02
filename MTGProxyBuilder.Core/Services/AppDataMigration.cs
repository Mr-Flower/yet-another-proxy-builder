namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// One‑time migration of the per‑user data folder after the rebrand
    /// (<c>%AppData%/MTGProxyBuilder</c> → <c>%AppData%/YetAnotherProxyBuilder</c>), so existing
    /// settings, libraries, projects cache and image caches carry over instead of starting fresh.
    /// Must run before any service computes its data paths.
    /// </summary>
    public static class AppDataMigration
    {
        private const string LegacyFolder = "MTGProxyBuilder";
        private const string CurrentFolder = "YetAnotherProxyBuilder";

        /// <summary>Renames the legacy data folder to the current one if it exists and the new one
        /// doesn't yet. Best‑effort: any failure is swallowed (the app just starts with a fresh folder).</summary>
        public static void Run()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string legacy = Path.Combine(appData, LegacyFolder);
                string current = Path.Combine(appData, CurrentFolder);

                if (Directory.Exists(legacy) && !Directory.Exists(current))
                    Directory.Move(legacy, current);
            }
            catch
            {
                // Never block startup over a best‑effort migration.
            }
        }
    }
}
