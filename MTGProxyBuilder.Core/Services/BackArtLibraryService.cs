using MTGProxyBuilder.Core.Models;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Services
{
    public class BackArtLibraryCatalog : ArtLibraryCatalog
    {
        [JsonProperty("defaultEntryId")]
        public string? DefaultEntryId { get; set; }
    }

    public class BackArtLibraryService : ArtLibraryServiceBase<BackArtLibraryCatalog>
    {
        private string? _defaultEntryId;

        public BackArtLibraryService(string? customDirectory = null)
            : base("BackArtLibrary", customDirectory) { }

        public string? DefaultEntryId => _defaultEntryId;

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

        public override bool Remove(string entryId)
        {
            bool removed = base.Remove(entryId);
            if (removed && _defaultEntryId == entryId)
            {
                _defaultEntryId = null;
                Save();
            }
            return removed;
        }

        protected override void OnLoadCatalog(BackArtLibraryCatalog catalog)
        {
            _defaultEntryId = catalog.DefaultEntryId;
        }

        protected override void OnSaveCatalog(BackArtLibraryCatalog catalog)
        {
            catalog.DefaultEntryId = _defaultEntryId;
        }

        protected override void OnMergeExistingCatalog(BackArtLibraryCatalog existingCatalog)
        {
            _defaultEntryId ??= existingCatalog.DefaultEntryId;
        }
    }
}
