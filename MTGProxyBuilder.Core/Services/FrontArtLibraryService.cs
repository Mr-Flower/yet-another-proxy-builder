using System.IO.Compression;
using MTGProxyBuilder.Core.Models;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Services
{
    public class FrontArtLibraryCatalog
    {
        [JsonProperty("entries")]
        public List<BackArtEntry> Entries { get; set; } = new();
    }

    public class FrontArtLibraryService
    {
        private string _libraryDirectory;
        private string _catalogPath;
        private List<BackArtEntry> _entries = new();

        public FrontArtLibraryService(string? customDirectory = null)
        {
            _libraryDirectory = customDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder", "FrontArtLibrary");
            Directory.CreateDirectory(_libraryDirectory);
            _catalogPath = Path.Combine(_libraryDirectory, "catalog.json");
            Load();
        }

        public string LibraryDirectory => _libraryDirectory;

        public IReadOnlyList<BackArtEntry> Entries => _entries.AsReadOnly();

        /// <summary>Search for library entries whose Name contains the card name.</summary>
        public List<BackArtEntry> SearchByCardName(string cardName)
        {
            if (string.IsNullOrWhiteSpace(cardName))
                return new List<BackArtEntry>();

            return _entries
                .Where(e => e.Name.Contains(cardName, StringComparison.OrdinalIgnoreCase)
                            && File.Exists(e.FilePath))
                .ToList();
        }

        private bool _batchMode;
        private HashSet<string>? _batchNameIndex;

        public void BeginBatch()
        {
            _batchMode = true;
            _batchNameIndex = new HashSet<string>(
                _entries.Select(e => e.Name),
                StringComparer.OrdinalIgnoreCase);
        }

        public void EndBatch()
        {
            _batchMode = false;
            _batchNameIndex = null;
            Save();
        }

        public BackArtEntry? AddFromFile(string sourceFilePath, string? displayName = null, string? contributor = null)
        {
            if (!File.Exists(sourceFilePath))
                return null;

            string name = displayName ?? Path.GetFileNameWithoutExtension(sourceFilePath);

            if (_batchNameIndex != null)
            {
                if (!_batchNameIndex.Add(name))
                    return _entries.FirstOrDefault(e =>
                        string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                var existing = _entries.FirstOrDefault(e =>
                    string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                    return existing;
            }

            string id = Guid.NewGuid().ToString("N")[..12];
            string ext = Path.GetExtension(sourceFilePath);
            string destFileName = $"{id}{ext}";
            string destPath = Path.Combine(_libraryDirectory, destFileName);

            File.Copy(sourceFilePath, destPath, overwrite: true);

            var entry = new BackArtEntry
            {
                Id = id,
                Name = name,
                FilePath = destPath,
                Source = contributor ?? "Local",
                AddedDate = DateTime.Now
            };

            _entries.Add(entry);
            if (!_batchMode)
                Save();
            return entry;
        }

        public bool Remove(string entryId)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == entryId);
            if (entry == null) return false;

            if (File.Exists(entry.FilePath))
            {
                try { File.Delete(entry.FilePath); }
                catch { }
            }

            _entries.Remove(entry);
            Save();
            return true;
        }

        public BackArtEntry? GetById(string id)
        {
            return _entries.FirstOrDefault(e => e.Id == id);
        }

        // ================================================================
        //  LIBRARY MANAGEMENT
        // ================================================================

        /// <summary>Moves all library files to a new directory and updates all paths.</summary>
        public void MoveToDirectory(string newDirectory, Action<int, int>? onProgress = null)
        {
            Directory.CreateDirectory(newDirectory);
            string oldDirectory = _libraryDirectory;

            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (File.Exists(entry.FilePath))
                {
                    string fileName = Path.GetFileName(entry.FilePath);
                    string destPath = Path.Combine(newDirectory, fileName);
                    File.Copy(entry.FilePath, destPath, overwrite: true);
                    entry.FilePath = destPath;
                }
                onProgress?.Invoke(i + 1, _entries.Count);
            }

            // Move Thumbnails subdirectory if it exists
            var oldThumbDir = Path.Combine(oldDirectory, "Thumbnails");
            if (Directory.Exists(oldThumbDir))
            {
                var newThumbDir = Path.Combine(newDirectory, "Thumbnails");
                Directory.CreateDirectory(newThumbDir);
                foreach (var file in Directory.GetFiles(oldThumbDir))
                    File.Copy(file, Path.Combine(newThumbDir, Path.GetFileName(file)), overwrite: true);
            }

            _libraryDirectory = newDirectory;
            _catalogPath = Path.Combine(newDirectory, "catalog.json");
            Save();

            // Clean up old directory
            try
            {
                if (Directory.Exists(oldDirectory) && !string.Equals(oldDirectory, newDirectory, StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(oldDirectory, recursive: true);
            }
            catch { }
        }

        /// <summary>Exports the library to a ZIP archive.</summary>
        public void ExportToZip(string zipFilePath, Action<int, int>? onProgress = null)
        {
            if (File.Exists(zipFilePath)) File.Delete(zipFilePath);

            using var zip = ZipFile.Open(zipFilePath, ZipArchiveMode.Create);

            if (File.Exists(_catalogPath))
                zip.CreateEntryFromFile(_catalogPath, "catalog.json", CompressionLevel.Optimal);

            var imageFiles = _entries.Where(e => File.Exists(e.FilePath)).ToList();
            for (int i = 0; i < imageFiles.Count; i++)
            {
                string fileName = Path.GetFileName(imageFiles[i].FilePath);
                zip.CreateEntryFromFile(imageFiles[i].FilePath, fileName, CompressionLevel.Optimal);
                onProgress?.Invoke(i + 1, imageFiles.Count);
            }
        }

        /// <summary>Imports entries from a ZIP archive into the current library. Returns count of new entries added.</summary>
        public int ImportFromZip(string zipFilePath, Action<int, int>? onProgress = null)
        {
            using var zip = ZipFile.OpenRead(zipFilePath);

            var catalogEntry = zip.GetEntry("catalog.json");
            if (catalogEntry == null) return 0;

            FrontArtLibraryCatalog? importedCatalog;
            using (var stream = catalogEntry.Open())
            using (var reader = new StreamReader(stream))
            {
                var json = reader.ReadToEnd();
                importedCatalog = JsonConvert.DeserializeObject<FrontArtLibraryCatalog>(json);
            }
            if (importedCatalog?.Entries == null) return 0;

            int added = 0;
            var imageEntries = importedCatalog.Entries;
            BeginBatch();
            try
            {
                for (int i = 0; i < imageEntries.Count; i++)
                {
                    var importEntry = imageEntries[i];
                    string fileName = Path.GetFileName(importEntry.FilePath);
                    var zipImageEntry = zip.GetEntry(fileName);
                    if (zipImageEntry == null) continue;

                    string tempPath = Path.Combine(Path.GetTempPath(), fileName);
                    try
                    {
                        zipImageEntry.ExtractToFile(tempPath, overwrite: true);
                        var result = AddFromFile(tempPath, importEntry.Name, importEntry.Source);
                        if (result != null) added++;
                    }
                    finally
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                    onProgress?.Invoke(i + 1, imageEntries.Count);
                }
            }
            finally { EndBatch(); }

            return added;
        }

        // ================================================================
        //  PERSISTENCE
        // ================================================================

        private void Load()
        {
            try
            {
                if (File.Exists(_catalogPath))
                {
                    string json = File.ReadAllText(_catalogPath);
                    var catalog = JsonConvert.DeserializeObject<FrontArtLibraryCatalog>(json);
                    if (catalog?.Entries != null)
                        _entries = catalog.Entries;

                    _entries.RemoveAll(e => !File.Exists(e.FilePath));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Front art library load error: {ex.Message}");
                _entries = new();
            }
        }

        private void Save()
        {
            try
            {
                var catalog = new FrontArtLibraryCatalog { Entries = _entries };
                string json = JsonConvert.SerializeObject(catalog, Formatting.Indented);
                File.WriteAllText(_catalogPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Front art library save error: {ex.Message}");
            }
        }
    }
}
