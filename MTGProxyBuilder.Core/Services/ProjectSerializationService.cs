using System.IO.Compression;
using MTGProxyBuilder.Core.Models;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// Saves/loads projects as self-contained ZIP archives (.mtgproj).
    /// Layout inside the archive:
    ///   project.json          — project metadata + card list (image paths are relative)
    ///   images/{filename}     — every referenced artwork file
    /// </summary>
    public class ProjectSerializationService
    {
        private const string ProjectJsonEntry = "project.json";
        private const string ImageFolder = "images/";
        private const int ProjectFileVersion = 2;

        // Temp directory for extracted images so the app can display them
        private readonly string _extractRoot;

        public ProjectSerializationService()
        {
            _extractRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "YetAnotherProxyBuilder", "ExtractedProjects");
            Directory.CreateDirectory(_extractRoot);
        }

        // ================================================================
        //  SAVE
        // ================================================================

        /// <summary>Saves the project to a self-contained .mtgproj archive. Returns false on failure
        /// (the original file is left untouched — the archive is written to a temp file first).</summary>
        public async Task<bool> SaveProjectAsync(ProjectModel project, string filePath)
        {
            try
            {
                string tempPath = filePath + ".tmp"; // write to temp, then atomically replace
                await Task.Run(() => WriteArchive(project, tempPath));
                ReplaceFile(tempPath, filePath);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save project error: {ex.Message}");
                return false;
            }
        }

        /// <summary>Writes the project's images and JSON into a fresh .mtgproj ZIP at the given path.</summary>
        private void WriteArchive(ProjectModel project, string archivePath)
        {
            using var stream = new FileStream(archivePath, FileMode.Create, FileAccess.Write);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

            var imageMap = BuildImageMap(project); // absolute path -> archive-relative name
            WriteImageEntries(zip, imageMap);
            WriteProjectJson(zip, project, imageMap);
        }

        /// <summary>Copies every referenced image file into the archive under its mapped name.</summary>
        private static void WriteImageEntries(ZipArchive zip, Dictionary<string, string> imageMap)
        {
            foreach (var (absolutePath, archiveName) in imageMap)
            {
                if (!File.Exists(absolutePath)) continue;
                var entry = zip.CreateEntry(ImageFolder + archiveName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(absolutePath);
                fileStream.CopyTo(entryStream);
            }
        }

        /// <summary>Serializes the project (with archive-relative image paths) into project.json.</summary>
        private void WriteProjectJson(ZipArchive zip, ProjectModel project, Dictionary<string, string> imageMap)
        {
            var wrapper = new ProjectFileWrapper
            {
                Version = ProjectFileVersion,
                Project = CloneWithRelativePaths(project, imageMap)
            };
            string json = JsonConvert.SerializeObject(wrapper, Formatting.Indented);
            var jsonEntry = zip.CreateEntry(ProjectJsonEntry, CompressionLevel.Optimal);
            using var jsonStream = new StreamWriter(jsonEntry.Open());
            jsonStream.Write(json);
        }

        private static void ReplaceFile(string tempPath, string finalPath)
        {
            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(tempPath, finalPath);
        }

        // ================================================================
        //  LOAD
        // ================================================================

        /// <summary>Loads a project from a .mtgproj archive. Returns null if the file is invalid.</summary>
        public Task<ProjectModel?> LoadProjectAsync(string filePath)
            => LoadProjectAsync(filePath, null);

        /// <summary>Loads a project, reporting progress through <paramref name="onProgress"/>.</summary>
        public async Task<ProjectModel?> LoadProjectAsync(string filePath, Action<string>? onProgress)
        {
            try
            {
                return await Task.Run(() => LoadArchive(filePath, onProgress));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load project error: {ex.Message}");
                return null;
            }
        }

        /// <summary>Reads the project JSON, extracts the bundled images, and resolves card art paths.</summary>
        private ProjectModel? LoadArchive(string filePath, Action<string>? onProgress)
        {
            onProgress?.Invoke("Reading project file...");
            using var stream = File.OpenRead(filePath);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

            onProgress?.Invoke("Parsing project data...");
            var project = ReadProject(zip);
            if (project == null) return null;

            string extractDir = PrepareExtractDir(filePath);
            ExtractImages(zip, extractDir, onProgress);

            onProgress?.Invoke("Resolving card artwork...");
            ResolveCardArtwork(project, extractDir);
            return project;
        }

        /// <summary>Deserializes project.json from the archive, or returns null if absent/invalid.</summary>
        private static ProjectModel? ReadProject(ZipArchive zip)
        {
            var jsonEntry = zip.GetEntry(ProjectJsonEntry);
            if (jsonEntry == null) return null;
            using var reader = new StreamReader(jsonEntry.Open());
            var wrapper = JsonConvert.DeserializeObject<ProjectFileWrapper>(reader.ReadToEnd());
            return wrapper?.Project;
        }

        /// <summary>
        /// Returns a clean per-file extraction folder. The folder name uses a STABLE hash so it's the same
        /// across app launches — otherwise the bleed/image caches (keyed by path) miss every restart and
        /// all artwork is reprocessed on reopen.
        /// </summary>
        private string PrepareExtractDir(string filePath)
        {
            string projectHash = Path.GetFileNameWithoutExtension(filePath) + "_" + StableHash.Hex(filePath);
            string extractDir = Path.Combine(_extractRoot, projectHash, "images");
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            Directory.CreateDirectory(extractDir);
            return extractDir;
        }

        /// <summary>Extracts every "images/..." archive entry into <paramref name="extractDir"/>.</summary>
        private static void ExtractImages(ZipArchive zip, string extractDir, Action<string>? onProgress)
        {
            var imageEntries = zip.Entries
                .Where(e => e.FullName.StartsWith(ImageFolder) && e.FullName != ImageFolder)
                .ToList();

            for (int i = 0; i < imageEntries.Count; i++)
            {
                onProgress?.Invoke($"Extracting images ({i + 1}/{imageEntries.Count})...");
                imageEntries[i].ExtractToFile(Path.Combine(extractDir, imageEntries[i].Name), overwrite: true);
            }
        }

        /// <summary>Rewrites each card's archive-relative art paths to the extracted absolute paths.</summary>
        private void ResolveCardArtwork(ProjectModel project, string extractDir)
        {
            foreach (var card in project.Cards)
            {
                card.ArtworkPath = ResolveImagePath(card.ArtworkPath, extractDir);
                card.BackArtworkPath = ResolveImagePath(card.BackArtworkPath, extractDir);
            }
        }

        // ================================================================
        //  HELPERS
        // ================================================================

        /// <summary>
        /// Builds a map from absolute file path → archive filename for every
        /// unique image referenced by the project's cards.
        /// </summary>
        private Dictionary<string, string> BuildImageMap(ProjectModel project)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int counter = 0;

            void Track(string? path)
            {
                if (string.IsNullOrEmpty(path) || map.ContainsKey(path)) return;
                string ext = Path.GetExtension(path);
                string fileName = SanitizeFileName(Path.GetFileName(path));
                // Keep the "mpc_" bleed marker at the FRONT of the archive name so MPCFill art is still
                // recognized as full-bleed after the project is reopened (BleedProcessor checks the
                // filename prefix). Without this the prefix was buried behind the counter and lost.
                string archiveName = fileName.StartsWith("mpc_", StringComparison.OrdinalIgnoreCase)
                    ? $"mpc_{counter++:D4}_{fileName[4..]}"
                    : $"{counter++:D4}_{fileName}";
                // Ensure uniqueness even if file names collide
                if (map.Values.Contains(archiveName))
                    archiveName = $"{counter++:D4}_{Guid.NewGuid():N}{ext}";
                map[path] = archiveName;
            }

            foreach (var card in project.Cards)
            {
                Track(card.ArtworkPath);
                Track(card.BackArtworkPath);
            }

            return map;
        }

        /// <summary>
        /// Returns a deep-ish copy of the project with card image paths
        /// replaced by archive-relative names (e.g. "images/0001_art.jpg").
        /// </summary>
        private ProjectModel CloneWithRelativePaths(ProjectModel source,
            Dictionary<string, string> imageMap)
        {
            return new ProjectModel
            {
                ProjectId = source.ProjectId,
                ProjectName = source.ProjectName,
                PageSettings = source.PageSettings,
                PrintSettings = source.PrintSettings,
                CreatedDate = source.CreatedDate,
                LastModified = DateTime.Now,
                Cards = source.Cards.Select(c => CloneCardWithRelativePaths(c, imageMap)).ToList()
            };
        }

        /// <summary>Copies a card, rewriting its image paths to archive-relative names.</summary>
        private static CardModel CloneCardWithRelativePaths(CardModel c, Dictionary<string, string> imageMap)
        {
            string Rel(string? absolutePath)
            {
                if (string.IsNullOrEmpty(absolutePath)) return string.Empty;
                return imageMap.TryGetValue(absolutePath, out var archiveName)
                    ? ImageFolder + archiveName
                    : string.Empty;
            }

            return new CardModel
            {
                CardId = c.CardId,
                Name = c.Name,
                ArtworkPath = Rel(c.ArtworkPath),
                BackArtworkPath = string.IsNullOrEmpty(c.BackArtworkPath) ? null : Rel(c.BackArtworkPath),
                OriginalBackArtworkPath = string.IsNullOrEmpty(c.OriginalBackArtworkPath) ? null : Rel(c.OriginalBackArtworkPath),
                Source = c.Source, // preserve MPCFill/Scryfall origin (drives bleed handling) across save/load
                FullResFrontUrl = c.FullResFrontUrl,
                FullResBackUrl = c.FullResBackUrl,
                ScryfallId = c.ScryfallId,
                Quantity = c.Quantity,
                IncludeBack = c.IncludeBack,
                OverlayText = c.OverlayText,
                ManaCost = c.ManaCost,
                CMC = c.CMC,
                TypeLine = c.TypeLine,
                OracleText = c.OracleText,
                Rarity = c.Rarity,
                Colors = c.Colors,
                ColorIdentity = c.ColorIdentity,
                SetCode = c.SetCode,
                SetName = c.SetName,
                CollectorNumber = c.CollectorNumber,
                Artist = c.Artist,
                Power = c.Power,
                Toughness = c.Toughness,
                Loyalty = c.Loyalty,
                Keywords = c.Keywords,
                DateAdded = c.DateAdded
            };
        }

        /// <summary>
        /// Converts an archive-relative path (e.g. "images/0001_art.jpg")
        /// back to an absolute path in the extraction directory, or returns
        /// the path as-is if it's already absolute and exists.
        /// </summary>
        private string ResolveImagePath(string? relativePath, string extractDir)
        {
            if (string.IsNullOrEmpty(relativePath)) return string.Empty;

            // Already an absolute path that exists (e.g. from an older format)
            if (Path.IsPathRooted(relativePath) && File.Exists(relativePath))
                return relativePath;

            // Strip the "images/" prefix if present
            string fileName = relativePath.StartsWith(ImageFolder)
                ? relativePath[ImageFolder.Length..]
                : Path.GetFileName(relativePath);

            string candidate = Path.Combine(extractDir, fileName);
            return File.Exists(candidate) ? candidate : string.Empty;
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        // ================================================================
        //  INTERNAL WRAPPER
        // ================================================================

        private class ProjectFileWrapper
        {
            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("project")]
            public ProjectModel? Project { get; set; }
        }
    }
}
