using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.ViewModels
{
    /// <summary>
    /// Coordinates deck import and MPCFill XML import operations.
    /// Extracted from MainViewModel to isolate import logic.
    /// </summary>
    public class ImportCoordinator
    {
        private readonly SearchCoordinator _search;
        private readonly DeckImportService _deckImport;
        private readonly MpcFillXmlImportService _xmlImport;
        private readonly YgoProDeckService _ygo; // fork-specific: Yu-Gi-Oh! source
        private readonly PiltoverArchiveService _piltoverArchive; // Riftbound source (ported from upstream)
        private readonly MTGProxyBuilder.UI.Services.ImageAdjustmentService _imageAdjust = new(); // fork-specific

        public ImportCoordinator(
            SearchCoordinator search,
            DeckImportService deckImport,
            MpcFillXmlImportService xmlImport,
            YgoProDeckService ygo,
            PiltoverArchiveService piltoverArchive)
        {
            _search = search;
            _deckImport = deckImport;
            _xmlImport = xmlImport;
            _ygo = ygo;
            _piltoverArchive = piltoverArchive;
        }

        private static readonly HashSet<string> BasicLands = new(StringComparer.OrdinalIgnoreCase)
            { "Plains", "Island", "Swamp", "Mountain", "Forest",
              "Wastes", "Snow-Covered Plains", "Snow-Covered Island",
              "Snow-Covered Swamp", "Snow-Covered Mountain", "Snow-Covered Forest" };

        public static bool IsBasicLand(string name) => BasicLands.Contains(name);

        // ================================================================
        //  DECK IMPORT
        // ================================================================

        public record DeckImportResult(
            List<CardModel> Cards,
            string DeckName,
            string SourceName,
            int SkippedDupes,
            int Failed);

        public async Task<(ImportedDeck? Deck, string? Error)> FetchDeckAsync(string url)
        {
            return await _deckImport.ImportAsync(url);
        }

        /// <summary>
        /// Resolves a deck/list into cards: skips duplicates (when requested), batch-resolves Scryfall
        /// metadata, then downloads artwork in parallel (bounded, cancellable) preserving deck order.
        /// </summary>
        public async Task<DeckImportResult> ImportDeckCardsAsync(
            ImportedDeck deck,
            IEnumerable<CardModel> existingCards,
            bool ignoreDuplicates,
            bool useMpcFill,
            int minDpi,
            bool fuzzySearch,
            bool useFavoritesOnly,
            Action<string>? onProgress = null,
            CancellationToken ct = default)
        {
            var toFetch = SelectEntriesToFetch(deck, existingCards, ignoreDuplicates, out int skippedDupes);

            // Batch-resolve Scryfall metadata in ONE round-trip per 75 cards (token "t:" entries are
            // resolved individually later, so they're excluded from the batch).
            onProgress?.Invoke($"Searching {toFetch.Count} card(s) on Scryfall...");
            var resolved = await _search.Scryfall.GetCardsByIdentifiersAsync(
                toFetch.Where(e => !IsTokenEntry(e.CardName)).Select(e => new CardIdentifier(
                    string.IsNullOrEmpty(e.ScryfallId) ? null : e.ScryfallId, e.CardName)));
            var (byId, byName) = BuildScryfallLookups(resolved);

            // Download artwork in parallel (bounded), writing each card into its deck-order slot.
            var slots = new CardModel?[toFetch.Count];
            int failed = 0, completed = 0;
            using var gate = new SemaphoreSlim(8);

            async Task RunAsync(int idx)
            {
                if (ct.IsCancellationRequested) return;
                await gate.WaitAsync(ct);
                try
                {
                    var card = await FetchCardAsync(
                        toFetch[idx], byId, byName, useMpcFill, minDpi, fuzzySearch, useFavoritesOnly);
                    if (card == null) Interlocked.Increment(ref failed); // couldn't resolve the entry
                    else slots[idx] = card;
                }
                finally
                {
                    gate.Release();
                    int n = Interlocked.Increment(ref completed);
                    onProgress?.Invoke($"Downloaded {n}/{toFetch.Count} card(s)...");
                }
            }

            try { await Task.WhenAll(Enumerable.Range(0, toFetch.Count).Select(RunAsync)); }
            catch (OperationCanceledException) { /* user cancelled — keep whatever was fetched so far */ }

            return new DeckImportResult(AssembleCards(slots), deck.Name, "", skippedDupes, failed);
        }

        /// <summary>
        /// Filters the deck's entries to those that still need fetching, honoring "skip duplicates"
        /// (basic lands only top up the missing quantity). Reports how many were skipped.
        /// </summary>
        private static List<DeckImportEntry> SelectEntriesToFetch(ImportedDeck deck,
            IEnumerable<CardModel> existingCards, bool ignoreDuplicates, out int skippedDupes)
        {
            skippedDupes = 0;
            var toFetch = new List<DeckImportEntry>();

            var existingByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (ignoreDuplicates)
                foreach (var c in existingCards)
                    existingByName[c.Name] = existingByName.GetValueOrDefault(c.Name) + c.Quantity;

            foreach (var original in deck.Entries)
            {
                var entry = original;
                if (ignoreDuplicates && existingByName.ContainsKey(entry.CardName))
                {
                    if (!IsBasicLand(entry.CardName)) { skippedDupes++; continue; }
                    int have = existingByName[entry.CardName];
                    if (entry.Quantity <= have) { skippedDupes++; continue; }
                    entry = new DeckImportEntry
                    {
                        CardName = entry.CardName,
                        Quantity = entry.Quantity - have, // basic land: only fetch the shortfall
                        ScryfallId = entry.ScryfallId,
                        Board = entry.Board
                    };
                }

                toFetch.Add(entry);
                if (ignoreDuplicates)
                    existingByName[entry.CardName] = existingByName.GetValueOrDefault(entry.CardName) + entry.Quantity;
            }
            return toFetch;
        }

        /// <summary>Indexes resolved Scryfall cards by id and by name (first printing per name wins).</summary>
        private static (Dictionary<string, ScryfallCard> ById, Dictionary<string, ScryfallCard> ByName)
            BuildScryfallLookups(IEnumerable<ScryfallCard> resolved)
        {
            var byId = new Dictionary<string, ScryfallCard>(StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, ScryfallCard>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in resolved)
            {
                if (!string.IsNullOrEmpty(c.Id)) byId[c.Id] = c;
                if (!byName.ContainsKey(c.Name)) byName[c.Name] = c;
            }
            return (byId, byName);
        }

        /// <summary>
        /// Resolves one entry to a Scryfall card and downloads its art. Returns null only when the entry
        /// can't be resolved at all (the caller counts that as a failure). The card is not yet adjusted.
        /// </summary>
        private async Task<CardModel?> FetchCardAsync(DeckImportEntry entry,
            IReadOnlyDictionary<string, ScryfallCard> byId, IReadOnlyDictionary<string, ScryfallCard> byName,
            bool useMpcFill, int minDpi, bool fuzzySearch, bool useFavoritesOnly)
        {
            bool isToken = IsTokenEntry(entry.CardName);
            var scryfallCard = isToken
                ? await _search.Scryfall.GetTokenByNameAsync(TokenName(entry.CardName))
                : await ResolveScryfallCard(entry, byId, byName);
            if (scryfallCard == null) return null;

            var (frontPath, usedMpcArt) = await DownloadFrontArt(
                entry, scryfallCard, isToken, useMpcFill, minDpi, fuzzySearch, useFavoritesOnly);
            string? backPath = scryfallCard.GetBackImageUrl() != null
                ? await _search.DownloadScryfallArtAsync(scryfallCard, back: true)
                : null;

            var card = scryfallCard.ToCardModel(frontPath ?? string.Empty, backPath);
            card.Quantity = entry.Quantity;
            if (usedMpcArt) card.Source = CardSource.MpcFill; // front art actually came from MPCFill
            return card;
        }

        /// <summary>Looks the entry up in the batch result (by id, then name), falling back to a fuzzy
        /// single Scryfall lookup for the few names the batch endpoint couldn't match.</summary>
        private async Task<ScryfallCard?> ResolveScryfallCard(DeckImportEntry entry,
            IReadOnlyDictionary<string, ScryfallCard> byId, IReadOnlyDictionary<string, ScryfallCard> byName)
        {
            ScryfallCard? card = null;
            if (!string.IsNullOrEmpty(entry.ScryfallId)) byId.TryGetValue(entry.ScryfallId!, out card);
            if (card == null) byName.TryGetValue(entry.CardName, out card);
            return card ?? await _search.Scryfall.GetCardByNameAsync(entry.CardName);
        }

        /// <summary>Downloads the front art: MPCFill when enabled and a name match exists, otherwise
        /// Scryfall (tokens are always Scryfall). Returns the path and whether MPCFill art was used.</summary>
        private async Task<(string? FrontPath, bool UsedMpcArt)> DownloadFrontArt(DeckImportEntry entry,
            ScryfallCard scryfallCard, bool isToken, bool useMpcFill, int minDpi, bool fuzzySearch, bool useFavoritesOnly)
        {
            if (useMpcFill && !isToken)
            {
                var (mpcResults, _) = await _search.SearchMpcFillForCard(
                    entry.CardName, minDpi, fuzzySearch, useFavoritesOnly);
                var bestMatch = mpcResults.FirstOrDefault(mc =>
                    mc.Name.Contains(entry.CardName, StringComparison.OrdinalIgnoreCase));
                if (bestMatch != null)
                {
                    var mpcPath = await _search.DownloadMpcFillArtAsync(bestMatch);
                    if (mpcPath != null) return (mpcPath, true);
                }
            }
            return (await _search.DownloadScryfallArtAsync(scryfallCard), false);
        }

        /// <summary>Collects the non-null fetched cards in deck order, applying the saved image
        /// adjustment to each (a no-op unless the user enabled auto-apply).</summary>
        private List<CardModel> AssembleCards(CardModel?[] slots)
        {
            var importedCards = new List<CardModel>();
            foreach (var card in slots)
            {
                if (card == null) continue;
                _imageAdjust.AutoApply(card);
                importedCards.Add(card);
            }
            return importedCards;
        }

        // "t: name" entries add a token (resolved via Scryfall type:token) instead of a card.
        private static bool IsTokenEntry(string name) =>
            name.TrimStart().StartsWith("t:", StringComparison.OrdinalIgnoreCase);

        private static string TokenName(string name)
        {
            string trimmed = name.TrimStart();
            int colon = trimmed.IndexOf(':');
            return colon >= 0 ? trimmed[(colon + 1)..].Trim() : trimmed.Trim();
        }

        // ================================================================
        //  YU-GI-OH! IMPORT (YGOPRODeck)  — fork-specific
        // ================================================================

        /// <summary>
        /// Resolves a pasted list into Yu-Gi-Oh! cards via YGOPRODeck: skips duplicates (when requested),
        /// then resolves + downloads each card's art in parallel (bounded, cancellable), preserving order.
        /// Mirrors <see cref="ImportDeckCardsAsync"/> but for the YGOPRODeck source.
        /// </summary>
        public async Task<DeckImportResult> ImportYuGiOhCardsAsync(
            ImportedDeck deck,
            IEnumerable<CardModel> existingCards,
            bool ignoreDuplicates,
            Action<string>? onProgress = null,
            CancellationToken ct = default)
        {
            var toFetch = SelectEntriesToFetch(deck, existingCards, ignoreDuplicates, out int skippedDupes);

            var slots = new CardModel?[toFetch.Count];
            int failed = 0, completed = 0;
            using var gate = new SemaphoreSlim(8);

            async Task RunAsync(int idx)
            {
                if (ct.IsCancellationRequested) return;
                await gate.WaitAsync(ct);
                try
                {
                    var card = await FetchYuGiOhCardAsync(toFetch[idx]);
                    if (card == null) Interlocked.Increment(ref failed);
                    else slots[idx] = card;
                }
                finally
                {
                    gate.Release();
                    int n = Interlocked.Increment(ref completed);
                    onProgress?.Invoke($"Downloaded {n}/{toFetch.Count} card(s)...");
                }
            }

            try { await Task.WhenAll(Enumerable.Range(0, toFetch.Count).Select(RunAsync)); }
            catch (OperationCanceledException) { /* user cancelled — keep whatever was fetched so far */ }

            return new DeckImportResult(AssembleCards(slots), deck.Name, "YGOPRODeck", skippedDupes, failed);
        }

        /// <summary>Resolves one entry to a Yu-Gi-Oh! card and downloads its art. Returns null only when
        /// the name can't be resolved or its image can't be fetched (counted as a failure).</summary>
        private async Task<CardModel?> FetchYuGiOhCardAsync(DeckImportEntry entry)
        {
            var card = await ResolveYuGiOhCard(entry.CardName);
            if (card == null) return null;

            var path = await _ygo.DownloadAndCacheImageAsync(card);
            if (path == null) return null;

            var model = card.ToCardModel(path);
            model.Quantity = entry.Quantity;
            return model;
        }

        /// <summary>Finds the best Yu-Gi-Oh! card for a name: an exact lookup first, then a fuzzy search
        /// preferring a case-insensitive name hit before falling back to the first result.</summary>
        private async Task<YgoProDeckCard?> ResolveYuGiOhCard(string name)
        {
            var (exact, _) = await _ygo.SearchCardAsync(name, fuzzy: false);
            if (exact.Count > 0) return exact[0];

            var (fuzzy, _) = await _ygo.SearchCardAsync(name, fuzzy: true);
            return fuzzy.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                   ?? fuzzy.FirstOrDefault();
        }

        // ================================================================
        //  RIFTBOUND / PILTOVER ARCHIVE IMPORT  — ported from upstream
        // ================================================================

        public record RiftboundImportResult(
            List<CardModel> Cards,
            string DeckName,
            int Downloaded,
            int Failed);

        public async Task<(RiftboundDeck? Deck, string? Error)> FetchRiftboundDeckAsync(string url)
        {
            return await _piltoverArchive.FetchDeckAsync(url);
        }

        /// <summary>
        /// Downloads every card of a Piltover Archive deck (legend, champions, battlefields, runes,
        /// main deck, sideboard, bench) with its chosen variant art. Unlike upstream (one CardModel per
        /// physical copy) this fork keeps N copies as ONE CardModel with Quantity=N.
        /// </summary>
        public async Task<RiftboundImportResult> ImportRiftboundCardsAsync(
            RiftboundDeck deck,
            Action<string>? onProgress = null,
            CancellationToken ct = default)
        {
            var importedCards = new List<CardModel>();
            int downloaded = 0, failed = 0;

            var allCards = deck.AllCards().ToList();

            for (int i = 0; i < allCards.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var entry = allCards[i];
                string cardName = entry.Card.Name;

                onProgress?.Invoke($"Downloading {i + 1}/{allCards.Count}: {cardName}" +
                    (entry.Quantity > 1 ? $" (x{entry.Quantity})" : "") + "...");

                string? artPath = await _piltoverArchive.DownloadCardImageAsync(entry);
                if (artPath == null)
                {
                    failed++;
                    continue;
                }

                string colors = string.Join(", ",
                    entry.Card.CardColors?.Select(cc => cc.Color.Name) ?? Enumerable.Empty<string>());
                string tags = string.Join(", ", entry.Card.Tags ?? new List<string>());

                var variant = entry.Card.CardVariants
                    .FirstOrDefault(v => v.Id == entry.VariantId)
                    ?? entry.Card.CardVariants.FirstOrDefault();

                var card = new CardModel
                {
                    Name = cardName,
                    ArtworkPath = artPath,
                    Quantity = entry.Quantity,
                    IsRiftbound = true,
                    TypeLine = entry.Card.Super != null
                        ? $"{entry.Card.Super} {entry.Card.Type}"
                        : entry.Card.Type,
                    OracleText = entry.Card.Description,
                    Rarity = variant?.Rarity ?? string.Empty,
                    Artist = variant?.Artist ?? string.Empty,
                    Colors = colors,
                    Keywords = tags,
                    CollectorNumber = variant?.VariantNumber ?? string.Empty,
                    DateAdded = DateTime.Now
                };

                _imageAdjust.AutoApply(card);
                importedCards.Add(card);
                downloaded++;
                await Task.Delay(50, CancellationToken.None);
            }

            return new RiftboundImportResult(importedCards, deck.Name, downloaded, failed);
        }

        // ================================================================
        //  MPCFILL XML IMPORT
        // ================================================================

        public record XmlImportResult(List<CardModel> Cards, int Downloaded, int Failed);

        public (MpcFillXmlProject? Project, string? Error) ParseXml(string filePath)
        {
            return _xmlImport.ParseXml(filePath);
        }

        public async Task<XmlImportResult> ImportXmlCardsAsync(
            MpcFillXmlProject project,
            Action<string>? onProgress = null)
        {
            var backsBySlot = new Dictionary<int, MpcFillXmlCard>();
            foreach (var back in project.Backs)
                foreach (var slot in back.Slots)
                    backsBySlot[slot] = back;

            var importedCards = new List<CardModel>();
            int downloaded = 0, failed = 0;

            for (int i = 0; i < project.Fronts.Count; i++)
            {
                var front = project.Fronts[i];
                string cardName = MpcFillXmlImportService.CleanCardName(front);
                int quantity = front.Slots.Count;

                onProgress?.Invoke($"Downloading {i + 1}/{project.Fronts.Count}: {cardName}" +
                    (quantity > 1 ? $" (x{quantity})" : "") + "...");

                string? frontPath = null;
                if (!string.IsNullOrEmpty(front.Id))
                    frontPath = await _xmlImport.DownloadImageByIdAsync(front.Id);
                if (frontPath == null) { failed++; continue; }

                string? backPath = null;
                var firstSlot = front.Slots.FirstOrDefault();
                if (backsBySlot.TryGetValue(firstSlot, out var backCard) && !string.IsNullOrEmpty(backCard.Id))
                {
                    onProgress?.Invoke($"Downloading back for: {cardName}...");
                    backPath = await _xmlImport.DownloadImageByIdAsync(backCard.Id);
                }
                if (backPath == null && !string.IsNullOrEmpty(project.CommonCardbackId))
                    backPath = await _xmlImport.DownloadImageByIdAsync(project.CommonCardbackId);

                var card = new CardModel
                {
                    Name = cardName,
                    ArtworkPath = frontPath,
                    BackArtworkPath = backPath,
                    IncludeBack = backPath != null,
                    Quantity = quantity,
                    Source = CardSource.MpcFill,
                    DateAdded = DateTime.Now
                };

                importedCards.Add(card);
                downloaded++;
                await Task.Delay(50);
            }

            return new XmlImportResult(importedCards, downloaded, failed);
        }

        // ================================================================
        //  MPCFILL CARD ADDITION
        // ================================================================

        public async Task<(CardModel? Card, string? Error)> AddMpcFillCardAsync(MpcFillCard mpcCard)
        {
            // Art is only cached (the cache key carries the "mpc_" bleed marker) — NOT auto-saved to
            // the library. The library is user-managed (explicit "+ Add to Library" only).
            var path = await _search.DownloadMpcFillArtAsync(mpcCard);

            var card = new CardModel
            {
                Name = mpcCard.Name.Split('(')[0].Trim(),
                ArtworkPath = path ?? string.Empty,
                Artist = mpcCard.Source,
                Source = CardSource.MpcFill,
                DateAdded = DateTime.Now
            };
            return (card, null);
        }
    }
}
