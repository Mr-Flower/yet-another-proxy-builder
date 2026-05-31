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
        private readonly FrontArtLibraryService _frontLibrary;
        private readonly MTGProxyBuilder.UI.Services.ImageAdjustmentService _imageAdjust = new(); // fork-specific

        public ImportCoordinator(
            SearchCoordinator search,
            DeckImportService deckImport,
            MpcFillXmlImportService xmlImport,
            FrontArtLibraryService frontLibrary)
        {
            _search = search;
            _deckImport = deckImport;
            _xmlImport = xmlImport;
            _frontLibrary = frontLibrary;
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
            int failed = 0, skippedDupes = 0;

            // ---- Phase 1: resolve duplicates up front (pure CPU, sequential) ----
            var existingByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (ignoreDuplicates)
                foreach (var c in existingCards)
                    existingByName[c.Name] = existingByName.GetValueOrDefault(c.Name) + c.Quantity;

            var toFetch = new List<DeckImportEntry>();
            foreach (var original in deck.Entries)
            {
                var entry = original;
                if (ignoreDuplicates && existingByName.ContainsKey(entry.CardName))
                {
                    if (IsBasicLand(entry.CardName))
                    {
                        int have = existingByName[entry.CardName];
                        if (entry.Quantity <= have) { skippedDupes++; continue; }
                        entry = new DeckImportEntry
                        {
                            CardName = entry.CardName,
                            Quantity = entry.Quantity - have,
                            ScryfallId = entry.ScryfallId,
                            Board = entry.Board
                        };
                    }
                    else { skippedDupes++; continue; }
                }

                toFetch.Add(entry);
                if (ignoreDuplicates)
                    existingByName[entry.CardName] = existingByName.GetValueOrDefault(entry.CardName) + entry.Quantity;
            }

            // ---- Phase 2: batch-resolve Scryfall metadata in ONE round-trip per 75 cards
            //               (was one /cards/named call per card — the old bottleneck).
            //               Token entries ("t: name") are resolved separately, so skip them here. ----
            onProgress?.Invoke($"Searching {toFetch.Count} card(s) on Scryfall...");
            var resolved = await _search.Scryfall.GetCardsByIdentifiersAsync(
                toFetch.Where(e => !IsTokenEntry(e.CardName)).Select(e => new CardIdentifier(
                    string.IsNullOrEmpty(e.ScryfallId) ? null : e.ScryfallId, e.CardName)));

            var byId = new Dictionary<string, ScryfallCard>(StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, ScryfallCard>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in resolved)
            {
                if (!string.IsNullOrEmpty(c.Id)) byId[c.Id] = c;
                if (!byName.ContainsKey(c.Name)) byName[c.Name] = c;
            }

            // ---- Phase 3: download artwork in parallel (bounded), preserving deck order ----
            var slots = new CardModel?[toFetch.Count];
            int completed = 0;
            using var gate = new SemaphoreSlim(8);

            async Task FetchAsync(int idx)
            {
                var entry = toFetch[idx];
                bool isToken = IsTokenEntry(entry.CardName);
                if (ct.IsCancellationRequested) return;
                await gate.WaitAsync(ct);
                try
                {
                    ScryfallCard? scryfallCard;
                    if (isToken)
                    {
                        // "t: name" -> look the token up directly (it isn't in the batch result).
                        scryfallCard = await _search.Scryfall.GetTokenByNameAsync(TokenName(entry.CardName));
                    }
                    else
                    {
                        scryfallCard = null;
                        if (!string.IsNullOrEmpty(entry.ScryfallId))
                            byId.TryGetValue(entry.ScryfallId!, out scryfallCard);
                        if (scryfallCard == null)
                            byName.TryGetValue(entry.CardName, out scryfallCard);
                        // Fuzzy single lookup only for the few names the batch endpoint couldn't match.
                        scryfallCard ??= await _search.Scryfall.GetCardByNameAsync(entry.CardName);
                    }
                    if (scryfallCard == null) { Interlocked.Increment(ref failed); return; }

                    string? frontPath = null;
                    string? backPath = null;

                    // Tokens come from Scryfall only; MPCFill name search wouldn't match a token.
                    if (useMpcFill && !isToken)
                    {
                        var (mpcResults, _) = await _search.SearchMpcFillForCard(
                            entry.CardName, minDpi, fuzzySearch, useFavoritesOnly);
                        var bestMatch = mpcResults.FirstOrDefault(mc =>
                            mc.Name.Contains(entry.CardName, StringComparison.OrdinalIgnoreCase));
                        if (bestMatch != null)
                            frontPath = await _search.DownloadMpcFillArtAsync(bestMatch);
                        frontPath ??= await _search.DownloadScryfallArtAsync(scryfallCard);
                    }
                    else
                    {
                        frontPath = await _search.DownloadScryfallArtAsync(scryfallCard);
                    }

                    if (scryfallCard.GetBackImageUrl() != null)
                        backPath = await _search.DownloadScryfallArtAsync(scryfallCard, back: true);

                    var card = scryfallCard.ToCardModel(frontPath ?? string.Empty, backPath);
                    card.Quantity = entry.Quantity;
                    slots[idx] = card;
                }
                finally
                {
                    gate.Release();
                    int n = Interlocked.Increment(ref completed);
                    onProgress?.Invoke($"Downloaded {n}/{toFetch.Count} card(s)...");
                }
            }

            // "t: name" entries add a token (resolved via Scryfall type:token) instead of a card.
            static bool IsTokenEntry(string name) =>
                name.TrimStart().StartsWith("t:", StringComparison.OrdinalIgnoreCase);
            static string TokenName(string name)
            {
                string t = name.TrimStart();
                int colon = t.IndexOf(':');
                return colon >= 0 ? t[(colon + 1)..].Trim() : t.Trim();
            }

            try { await Task.WhenAll(Enumerable.Range(0, toFetch.Count).Select(FetchAsync)); }
            catch (OperationCanceledException) { /* user cancelled — keep whatever was fetched so far */ }

            // ---- Phase 4: assemble in deck order. AutoApply touches a shared on-disk store,
            //               so it runs sequentially (and is a no-op unless the user enabled it). ----
            var importedCards = new List<CardModel>();
            foreach (var card in slots)
            {
                if (card == null) continue;
                _imageAdjust.AutoApply(card); // fork-specific: black-point etc. on Scryfall imports
                importedCards.Add(card);
            }

            return new DeckImportResult(importedCards, deck.Name, "", skippedDupes, failed);
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
            var path = await _search.DownloadMpcFillArtAsync(mpcCard);

            if (path != null)
            {
                string libName = $"{mpcCard.Name} [{mpcCard.Source}]";
                var entry = _frontLibrary.AddFromFile(path, libName, mpcCard.Source);
                if (entry != null)
                    _frontLibrary.ApplyMpcFillDefaults(entry.Id, mpcCard.Source);
            }

            var card = new CardModel
            {
                Name = mpcCard.Name.Split('(')[0].Trim(),
                ArtworkPath = path ?? string.Empty,
                Artist = mpcCard.Source,
                DateAdded = DateTime.Now
            };
            return (card, null);
        }
    }
}
