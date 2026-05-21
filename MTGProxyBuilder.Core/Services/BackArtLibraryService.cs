using System.IO.Compression;
using MTGProxyBuilder.Core.Models;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Services
{
    public class BackArtLibraryCatalog
    {
        [JsonProperty("entries")]
        public List<BackArtEntry> Entries { get; set; } = new();

        [JsonProperty("defaultEntryId")]
        public string? DefaultEntryId { get; set; }
    }

    public class BackArtLibraryService
    {
        private string _libraryDirectory;
        private string _catalogPath;
        private List<BackArtEntry> _entries = new();
        private string? _defaultEntryId;

        public BackArtLibraryService(string? customDirectory = null)
        {
            _libraryDirectory = customDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder", "BackArtLibrary");
            Directory.CreateDirectory(_libraryDirectory);
            _catalogPath = Path.Combine(_libraryDirectory, "catalog.json");
            Load();
        }

        public string LibraryDirectory => _libraryDirectory;

        public IReadOnlyList<BackArtEntry> Entries => _entries.AsReadOnly();

        public string? DefaultEntryId => _defaultEntryId;

        /// <summary>Returns the default back art file path, or null if none is set.</summary>
        public string? DefaultBackArtPath
        {
            get
            {
                if (_defaultEntryId == null) return null;
                var entry = _entries.FirstOrDefault(e => e.Id == _defaultEntryId);
                return entry != null && File.Exists(entry.FilePath) ? entry.FilePath : null;
            }
        }

        public void SetDefault(string? entryId)
        {
            _defaultEntryId = entryId;
            Save();
        }

        public bool IsDefault(string entryId) => _defaultEntryId == entryId;

        private bool _batchMode;
        private HashSet<string>? _batchNameIndex;

        /// <summary>Begin a batch operation. Suppresses Save() until EndBatch() is called.</summary>
        public void BeginBatch()
        {
            _batchMode = true;
            _batchNameIndex = new HashSet<string>(
                _entries.Select(e => e.Name),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>End a batch operation and persist all changes at once.</summary>
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
            if (_defaultEntryId == entryId)
                _defaultEntryId = null;
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

            // Add catalog
            if (File.Exists(_catalogPath))
                zip.CreateEntryFromFile(_catalogPath, "catalog.json", CompressionLevel.Optimal);

            // Add all image files
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

            // Read catalog from ZIP to get entry metadata
            var catalogEntry = zip.GetEntry("catalog.json");
            if (catalogEntry == null) return 0;

            BackArtLibraryCatalog? importedCatalog;
            using (var stream = catalogEntry.Open())
            using (var reader = new StreamReader(stream))
            {
                var json = reader.ReadToEnd();
                importedCatalog = JsonConvert.DeserializeObject<BackArtLibraryCatalog>(json);
            }
            if (importedCatalog?.Entries == null) return 0;

            var imageEntries = importedCatalog.Entries;
            int countBefore = _entries.Count;
            BeginBatch();
            try
            {
                for (int i = 0; i < imageEntries.Count; i++)
                {
                    var importEntry = imageEntries[i];
                    string fileName = Path.GetFileName(importEntry.FilePath);
                    var zipImageEntry = zip.GetEntry(fileName);
                    if (zipImageEntry == null) continue;

                    // Extract to temp, then add via normal flow
                    string tempPath = Path.Combine(Path.GetTempPath(), fileName);
                    try
                    {
                        zipImageEntry.ExtractToFile(tempPath, overwrite: true);
                        AddFromFile(tempPath, importEntry.Name, importEntry.Source);
                    }
                    finally
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                    onProgress?.Invoke(i + 1, imageEntries.Count);
                }
            }
            finally { EndBatch(); }

            return _entries.Count - countBefore;
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

                    // Try new format first
                    var catalog = JsonConvert.DeserializeObject<BackArtLibraryCatalog>(json);
                    if (catalog?.Entries != null && catalog.Entries.Count > 0)
                    {
                        _entries = catalog.Entries;
                        _defaultEntryId = catalog.DefaultEntryId;
                    }
                    else
                    {
                        // Fall back to old format (just a list)
                        _entries = JsonConvert.DeserializeObject<List<BackArtEntry>>(json) ?? new();
                    }

                    _entries.RemoveAll(e => !File.Exists(e.FilePath));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Back art library load error: {ex.Message}");
                _entries = new();
            }
        }

        private void Save()
        {
            try
            {
                var catalog = new BackArtLibraryCatalog
                {
                    Entries = _entries,
                    DefaultEntryId = _defaultEntryId
                };
                string json = JsonConvert.SerializeObject(catalog, Formatting.Indented);
                File.WriteAllText(_catalogPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Back art library save error: {ex.Message}");
            }
        }
    }
}
