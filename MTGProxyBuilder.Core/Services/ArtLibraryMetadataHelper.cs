using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// Helper to populate BackArtEntry metadata from Scryfall or MPCFill card data.
    /// </summary>
    public static class ArtLibraryMetadataHelper
    {
        /// <summary>Populates full metadata on a library entry from a ScryfallCard.</summary>
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

        /// <summary>
        /// Applies MPCFill-specific defaults for entries without Scryfall metadata.
        /// Sets SetCode=MPC, SetName=MPCFill.com, Artist=source contributor name.
        /// </summary>
        public static void ApplyMpcFillDefaults<TCatalog>(this ArtLibraryServiceBase<TCatalog> library,
            string entryId, string source) where TCatalog : ArtLibraryCatalog, new()
        {
            var entry = library.GetById(entryId);
            if (entry == null) return;

            // Only apply defaults to fields that are still empty (don't overwrite Scryfall data)
            library.SetMetadata(entryId,
                setCode: string.IsNullOrEmpty(entry.SetCode) ? "MPC" : null,
                setName: string.IsNullOrEmpty(entry.SetName) ? "MPCFill.com" : null,
                artist: string.IsNullOrEmpty(entry.Artist) ? source : null);
        }
    }
}
