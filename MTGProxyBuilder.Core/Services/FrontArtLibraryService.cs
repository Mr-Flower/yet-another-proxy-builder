using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Core.Services
{
    public class FrontArtLibraryCatalog : ArtLibraryCatalog { }

    public class FrontArtLibraryService : ArtLibraryServiceBase<FrontArtLibraryCatalog>
    {
        public FrontArtLibraryService(string? customDirectory = null)
            : base("FrontArtLibrary", customDirectory) { }

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
    }
}
