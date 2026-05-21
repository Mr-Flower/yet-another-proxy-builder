using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// Helper to populate BackArtEntry metadata from Scryfall card data.
    /// Used when adding art to libraries from search results.
    /// </summary>
    public static class ArtLibraryMetadataHelper
    {
        /// <summary>Populates metadata on a library entry from a ScryfallCard. Returns the entry for chaining.</summary>
        public static void ApplyMetadata<TCatalog>(this ArtLibraryServiceBase<TCatalog> library,
            string entryId, ScryfallCard card) where TCatalog : ArtLibraryCatalog, new()
        {
            string manaCost = card.ManaCost ?? card.CardFaces?.FirstOrDefault()?.ManaCost ?? "";
            string typeLine = card.TypeLine ?? card.CardFaces?.FirstOrDefault()?.TypeLine ?? "";
            string oracleText = card.OracleText ?? card.CardFaces?.FirstOrDefault()?.OracleText ?? "";
            string power = card.Power ?? card.CardFaces?.FirstOrDefault()?.Power ?? "";
            string toughness = card.Toughness ?? card.CardFaces?.FirstOrDefault()?.Toughness ?? "";
            string loyalty = card.Loyalty ?? card.CardFaces?.FirstOrDefault()?.Loyalty ?? "";

            library.SetMetadata(entryId,
                typeLine: typeLine,
                oracleText: oracleText,
                manaCost: manaCost,
                cmc: card.CMC,
                rarity: card.Rarity ?? "",
                colors: card.Colors != null ? string.Join(",", card.Colors) : "",
                colorIdentity: card.ColorIdentity != null ? string.Join(",", card.ColorIdentity) : "",
                setCode: card.SetCode,
                setName: card.SetName,
                artist: card.Artist ?? "",
                power: power,
                toughness: toughness,
                loyalty: loyalty,
                keywords: card.Keywords != null ? string.Join(",", card.Keywords) : "",
                collectorNumber: card.CollectorNumber);
        }

        /// <summary>Populates basic metadata on a library entry from an MpcFillCard (limited data available).</summary>
        public static void ApplyMetadata<TCatalog>(this ArtLibraryServiceBase<TCatalog> library,
            string entryId, MpcFillCard card) where TCatalog : ArtLibraryCatalog, new()
        {
            // MPCFill only provides name, source, DPI, and language - no card metadata
            // But we can extract the card name from the display name for future Scryfall lookups
        }
    }
}
