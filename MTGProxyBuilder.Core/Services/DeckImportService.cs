namespace MTGProxyBuilder.Core.Services
{
    public enum DeckSource { Unknown, Moxfield, Archidekt }

    public class DeckImportEntry
    {
        public string CardName { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
        public string? ScryfallId { get; set; }
        public string Board { get; set; } = string.Empty;
    }

    public class ImportedDeck
    {
        public string Name { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public DeckSource Source { get; set; }
        public List<DeckImportEntry> Entries { get; set; } = new();
    }

    /// <summary>
    /// Auto-detects the deck source from a URL and fetches the deck list.
    /// </summary>
    public class DeckImportService
    {
        private readonly MoxfieldService _moxfield;
        private readonly ArchidektService _archidekt;

        public DeckImportService(MoxfieldService moxfield, ArchidektService archidekt)
        {
            _moxfield = moxfield;
            _archidekt = archidekt;
        }

        public static DeckSource DetectSource(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return DeckSource.Unknown;
            url = url.Trim().ToLowerInvariant();

            if (url.Contains("moxfield.com")) return DeckSource.Moxfield;
            if (url.Contains("archidekt.com")) return DeckSource.Archidekt;

            return DeckSource.Unknown;
        }

        /// <summary>
        /// Parses a pasted text decklist into entries. Accepts common formats, one card
        /// per line:
        ///   "2 Sol Ring", "2x Sol Ring", "Sol Ring", "1 Sol Ring (C21) 263"
        /// Section headers ("Deck", "Sidebar:", "Commander", etc.), blank lines and lines
        /// starting with '#' or '//' are ignored. The trailing set/collector hint
        /// "(SET) 123" is stripped from the name. Quantities are summed for duplicate names.
        /// </summary>
        public static ImportedDeck ParseTextList(string text)
        {
            var deck = new ImportedDeck { Name = "Pasted list", Source = DeckSource.Unknown };
            if (string.IsNullOrWhiteSpace(text)) return deck;

            var byName = new Dictionary<string, DeckImportEntry>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#") || line.StartsWith("//")) continue;

                // Skip common section headers (no quantity, ends with ':' or known words).
                var lower = line.ToLowerInvariant().TrimEnd(':');
                if (lower is "deck" or "sideboard" or "sideboar" or "commander"
                    or "maybeboard" or "companion" or "tokens") continue;

                int qty = 1;
                string name = line;

                // Leading quantity: "2 ", "2x ", "2X "
                var m = System.Text.RegularExpressions.Regex.Match(line, @"^(\d+)\s*[xX]?\s+(.+)$");
                if (m.Success)
                {
                    qty = int.TryParse(m.Groups[1].Value, out var q) && q > 0 ? q : 1;
                    name = m.Groups[2].Value.Trim();
                }

                // Strip trailing set/collector hint: "(C21) 263", "(C21)", " 263"
                name = System.Text.RegularExpressions.Regex.Replace(
                    name, @"\s*\([A-Za-z0-9]{2,5}\)\s*[A-Za-z0-9\-]*\s*$", "").Trim();
                // Strip a bare trailing collector number left over (e.g. "Sol Ring 263")
                // only if preceded by more than one word, to avoid eating real names.

                if (name.Length == 0) continue;

                if (byName.TryGetValue(name, out var existing))
                    existing.Quantity += qty;
                else
                {
                    byName[name] = new DeckImportEntry { CardName = name, Quantity = qty };
                    order.Add(name);
                }
            }

            deck.Entries = order.Select(n => byName[n]).ToList();
            return deck;
        }

        public async Task<(ImportedDeck? Deck, string? Error)> ImportAsync(string url)
        {
            var source = DetectSource(url);

            switch (source)
            {
                case DeckSource.Moxfield:
                {
                    string? deckId = MoxfieldService.ParseDeckId(url);
                    if (string.IsNullOrEmpty(deckId))
                        return (null, "Could not extract deck ID from Moxfield URL.");

                    var (deck, error) = await _moxfield.FetchDeckAsync(deckId);
                    if (deck == null) return (null, error);

                    return (new ImportedDeck
                    {
                        Name = deck.Name,
                        Format = deck.Format,
                        Source = DeckSource.Moxfield,
                        Entries = deck.Entries.Select(e => new DeckImportEntry
                        {
                            CardName = e.CardName,
                            Quantity = e.Quantity,
                            ScryfallId = e.ScryfallId,
                            Board = e.Board
                        }).ToList()
                    }, null);
                }

                case DeckSource.Archidekt:
                {
                    string? deckId = ArchidektService.ParseDeckId(url);
                    if (string.IsNullOrEmpty(deckId))
                        return (null, "Could not extract deck ID from Archidekt URL.");

                    var (deck, error) = await _archidekt.FetchDeckAsync(deckId);
                    if (deck == null) return (null, error);

                    return (new ImportedDeck
                    {
                        Name = deck.Name,
                        Format = deck.Format,
                        Source = DeckSource.Archidekt,
                        Entries = deck.Entries.Select(e => new DeckImportEntry
                        {
                            CardName = e.CardName,
                            Quantity = e.Quantity,
                            ScryfallId = e.ScryfallId,
                            Board = e.Category
                        }).ToList()
                    }, null);
                }

                default:
                    return (null, "Unrecognized URL. Paste a deck URL from Moxfield or Archidekt.");
            }
        }
    }
}
