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
        private readonly string _libraryDirectory;
        private readonly string _catalogPath;
        private List<BackArtEntry> _entries = new();

        public FrontArtLibraryService()
        {
            _libraryDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder", "FrontArtLibrary");
            Directory.CreateDirectory(_libraryDirectory);
            _catalogPath = Path.Combine(_libraryDirectory, "catalog.json");
            Load();
        }

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
