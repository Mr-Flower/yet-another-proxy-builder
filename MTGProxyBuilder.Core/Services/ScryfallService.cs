using MTGProxyBuilder.Core.Models;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Services
{
    public class ScryfallCardSearchResult
    {
        [JsonProperty("data")]
        public List<ScryfallCard>? Data { get; set; }

        [JsonProperty("has_more")]
        public bool HasMore { get; set; }

        [JsonProperty("next_page")]
        public string? NextPage { get; set; }

        [JsonProperty("total_cards")]
        public int TotalCards { get; set; }
    }

    /// <summary>Response shape of the /cards/collection batch endpoint.</summary>
    public class ScryfallCollectionResult
    {
        [JsonProperty("data")]
        public List<ScryfallCard>? Data { get; set; }
    }

    /// <summary>One identifier for a batch (/cards/collection) lookup: a Scryfall id or an exact name.</summary>
    public record CardIdentifier(string? ScryfallId, string Name);

    public class ScryfallCard
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("mana_cost")]
        public string? ManaCost { get; set; }

        [JsonProperty("cmc")]
        public float CMC { get; set; }

        [JsonProperty("type_line")]
        public string? TypeLine { get; set; }

        [JsonProperty("oracle_text")]
        public string? OracleText { get; set; }

        [JsonProperty("colors")]
        public List<string>? Colors { get; set; }

        [JsonProperty("color_identity")]
        public List<string>? ColorIdentity { get; set; }

        [JsonProperty("keywords")]
        public List<string>? Keywords { get; set; }

        [JsonProperty("power")]
        public string? Power { get; set; }

        [JsonProperty("toughness")]
        public string? Toughness { get; set; }

        [JsonProperty("loyalty")]
        public string? Loyalty { get; set; }

        [JsonProperty("rarity")]
        public string? Rarity { get; set; }

        [JsonProperty("artist")]
        public string? Artist { get; set; }

        [JsonProperty("set")]
        public string SetCode { get; set; } = string.Empty;

        [JsonProperty("set_name")]
        public string SetName { get; set; } = string.Empty;

        [JsonProperty("collector_number")]
        public string CollectorNumber { get; set; } = string.Empty;

        [JsonProperty("released_at")]
        public string? ReleasedAt { get; set; }

        [JsonProperty("image_uris")]
        public Dictionary<string, string>? ImageUris { get; set; }

        [JsonProperty("card_faces")]
        public List<CardFace>? CardFaces { get; set; }

        // Related cards (tokens, meld parts, etc.). Tokens have Component == "token".
        [JsonProperty("all_parts")]
        public List<ScryfallRelatedPart>? AllParts { get; set; }

        public string? GetImageUrl(string size = "large")
        {
            if (ImageUris != null && ImageUris.TryGetValue(size, out var url))
                return url;
            if (CardFaces?.Count > 0 && CardFaces[0].ImageUris != null)
                return CardFaces[0].ImageUris!.TryGetValue(size, out var faceUrl) ? faceUrl : null;
            return null;
        }

        public string? GetBackImageUrl(string size = "large")
        {
            if (CardFaces?.Count > 1 && CardFaces[1].ImageUris != null)
                return CardFaces[1].ImageUris!.TryGetValue(size, out var url) ? url : null;
            return null;
        }

        /// <summary>Small thumbnail URL (handles double-faced cards). For binding in result lists.</summary>
        [JsonIgnore]
        public string? SmallImageUrl => GetImageUrl("small");

        /// <summary>Normal-size image URL (handles double-faced cards). For binding in previews.</summary>
        [JsonIgnore]
        public string? NormalImageUrl => GetImageUrl("normal");

        /// <summary>Populate a CardModel with all available Scryfall metadata.</summary>
        public CardModel ToCardModel(string artworkPath, string? backArtworkPath)
        {
            bool hasBack = backArtworkPath != null;

            // For double-faced cards, gather text from both faces
            string manaCost = ManaCost ?? CardFaces?.FirstOrDefault()?.ManaCost ?? string.Empty;
            string typeLine = TypeLine ?? CardFaces?.FirstOrDefault()?.TypeLine ?? string.Empty;
            string oracleText = OracleText ?? CardFaces?.FirstOrDefault()?.OracleText ?? string.Empty;
            string power = Power ?? CardFaces?.FirstOrDefault()?.Power ?? string.Empty;
            string toughness = Toughness ?? CardFaces?.FirstOrDefault()?.Toughness ?? string.Empty;
            string loyalty = Loyalty ?? CardFaces?.FirstOrDefault()?.Loyalty ?? string.Empty;

            return new CardModel
            {
                Name = Name,
                ScryfallId = Id,
                ArtworkPath = artworkPath,
                BackArtworkPath = backArtworkPath,
                OriginalBackArtworkPath = backArtworkPath,
                // Full-res URLs for on-demand download at PDF export (ArtworkPath holds a compressed copy).
                FullResFrontUrl = GetImageUrl("large"),
                FullResBackUrl = GetBackImageUrl("large"),
                IncludeBack = hasBack,
                ManaCost = manaCost,
                CMC = CMC,
                TypeLine = typeLine,
                OracleText = oracleText,
                Rarity = Rarity ?? string.Empty,
                Colors = Colors != null ? string.Join(",", Colors) : string.Empty,
                ColorIdentity = ColorIdentity != null ? string.Join(",", ColorIdentity) : string.Empty,
                SetCode = SetCode,
                SetName = SetName,
                CollectorNumber = CollectorNumber,
                Artist = Artist ?? string.Empty,
                Power = power,
                Toughness = toughness,
                Loyalty = loyalty,
                Keywords = Keywords != null ? string.Join(",", Keywords) : string.Empty,
                DateAdded = DateTime.Now,
                Source = CardSource.Scryfall // fork-specific
            };
        }

        public override string ToString() => $"{Name} ({SetName} #{CollectorNumber})";
    }

    public class CardFace
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("mana_cost")]
        public string? ManaCost { get; set; }

        [JsonProperty("type_line")]
        public string? TypeLine { get; set; }

        [JsonProperty("oracle_text")]
        public string? OracleText { get; set; }

        [JsonProperty("power")]
        public string? Power { get; set; }

        [JsonProperty("toughness")]
        public string? Toughness { get; set; }

        [JsonProperty("loyalty")]
        public string? Loyalty { get; set; }

        [JsonProperty("image_uris")]
        public Dictionary<string, string>? ImageUris { get; set; }
    }

    /// <summary>An entry in a card's <c>all_parts</c> (related cards: tokens, meld halves, etc.).</summary>
    public class ScryfallRelatedPart
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>"token", "meld_part", "meld_result", "combo_piece", etc.</summary>
        [JsonProperty("component")]
        public string Component { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("type_line")]
        public string? TypeLine { get; set; }

        [JsonProperty("uri")]
        public string Uri { get; set; } = string.Empty;
    }

    public class ScryfallService
    {
        private readonly HttpClient _httpClient;
        private readonly ImageCacheService _imageCache;

        public ScryfallService(ImageCacheService imageCache)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MTGProxyBuilder/1.0");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _imageCache = imageCache;
        }

        /// <param name="uniquePrints">
        /// When false (default), returns one result per card (unique=cards) — the standard
        /// printing, used by the main search box. When true, returns every printing/version
        /// (unique=prints, newest first) — used by the art/version selector.
        /// </param>
        public async Task<(List<ScryfallCard> Cards, string? Error)> SearchCardAsync(string cardName, bool uniquePrints = false)
        {
            try
            {
                string encoded = System.Net.WebUtility.UrlEncode(cardName);
                string unique = uniquePrints ? "prints" : "cards";
                string? url = $"https://api.scryfall.com/cards/search?q={encoded}&unique={unique}&order=released";
                var allCards = new List<ScryfallCard>();

                while (url != null)
                {
                    var response = await _httpClient.GetAsync(url);

                    if (!response.IsSuccessStatusCode)
                    {
                        string body = await response.Content.ReadAsStringAsync();
                        if (allCards.Count > 0) break; // return what we have from prior pages
                        return (new(), $"Scryfall returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 200)]}");
                    }

                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<ScryfallCardSearchResult>(content);
                    if (result?.Data != null)
                        allCards.AddRange(result.Data);

                    // Scryfall requires 50-100ms between requests
                    url = result is { HasMore: true, NextPage: not null } ? result.NextPage : null;
                    if (url != null)
                        await Task.Delay(100);
                }

                return (allCards, null);
            }
            catch (HttpRequestException ex)
            {
                return (new(), $"Network error: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                return (new(), "Request timed out");
            }
            catch (Exception ex)
            {
                return (new(), $"Error: {ex.Message}");
            }
        }

        private string PrintingsCacheFile(string cardName)
        {
            string safe = string.Concat(cardName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
            if (safe.Length > 80) safe = safe[..80];
            return Path.Combine(_imageCache.CacheDirectory, $"printings_{safe}.json");
        }

        /// <summary>True if this card's full printing list is already cached on disk.</summary>
        public bool HasCachedPrintings(string cardName) => File.Exists(PrintingsCacheFile(cardName));

        /// <summary>
        /// Returns every printing of the exact card name, cached to disk so reopening the art
        /// selector doesn't re-query Scryfall every time. Falls back to a live unique=prints search
        /// on a cache miss and persists the result.
        /// </summary>
        public async Task<List<ScryfallCard>> GetAllPrintingsAsync(string cardName)
        {
            string cacheFile = PrintingsCacheFile(cardName);
            if (File.Exists(cacheFile))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(cacheFile);
                    var cached = JsonConvert.DeserializeObject<List<ScryfallCard>>(json);
                    if (cached is { Count: > 0 }) return cached;
                }
                catch { /* corrupt cache -> fall through to live search */ }
            }

            var (cards, _) = await SearchCardAsync($"!\"{cardName}\"", uniquePrints: true);
            if (cards.Count > 0)
            {
                try { await File.WriteAllTextAsync(cacheFile, JsonConvert.SerializeObject(cards)); }
                catch { /* cache write is best-effort */ }
            }
            return cards;
        }

        /// <summary>Downloads an arbitrary image URL into the shared image cache under the given key
        /// (reusing the configured HttpClient). Returns the cached path, or null on failure. Used to
        /// fetch full-resolution art on demand at PDF export.</summary>
        public async Task<string?> DownloadUrlToCacheAsync(string? url, string cacheKey)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var cached = _imageCache.GetCachedImagePath(cacheKey);
            if (cached != null) return cached;
            return await _imageCache.CacheImageFromUrlAsync(_httpClient, url, cacheKey);
        }

        /// <summary>Cache key for a card image at a given size/face (shared by download + cache-probe).</summary>
        private static string ImageCacheKey(ScryfallCard card, bool back, string size)
        {
            string sizeSuffix = size == "large" ? "" : $"_{size}";
            return back ? $"{card.Id}_back{sizeSuffix}" : $"{card.Id}{sizeSuffix}";
        }

        /// <summary>Cached path for this card's image if already on disk, else null. Lets callers (the
        /// art selector) show already-downloaded art instantly without re-entering the download path.</summary>
        public string? GetCachedImagePath(ScryfallCard card, bool back = false, string size = "large")
            => _imageCache.GetCachedImagePath(ImageCacheKey(card, back, size));

        public async Task<string?> DownloadAndCacheImageAsync(ScryfallCard card, bool back = false, string size = "large")
        {
            string cacheKey = ImageCacheKey(card, back, size);

            var cached = _imageCache.GetCachedImagePath(cacheKey);
            if (cached != null) return cached;

            string? imageUrl = back ? card.GetBackImageUrl(size) : card.GetImageUrl(size);
            if (imageUrl == null) return null;

            return await _imageCache.CacheImageFromUrlAsync(_httpClient, imageUrl, cacheKey);
        }

        /// <summary>
        /// Resolves many cards in one round-trip via Scryfall's /cards/collection endpoint
        /// (up to 75 identifiers per request), instead of one /cards/named call per card.
        /// This is the fast path for deck/list imports. Identifiers are exact names or Scryfall ids.
        /// Names the batch endpoint can't match (e.g. typos) are simply absent from the result —
        /// callers fall back to a fuzzy single lookup for those few.
        /// </summary>
        public async Task<List<ScryfallCard>> GetCardsByIdentifiersAsync(IEnumerable<CardIdentifier> identifiers)
        {
            var list = identifiers.ToList();
            var all = new List<ScryfallCard>();

            for (int offset = 0; offset < list.Count; offset += 75)
            {
                var chunk = list.Skip(offset).Take(75).Select(id => id.ScryfallId != null
                    ? (object)new { id = id.ScryfallId }
                    : new { name = id.Name }).ToList();

                try
                {
                    string body = JsonConvert.SerializeObject(new { identifiers = chunk });
                    using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync("https://api.scryfall.com/cards/collection", content);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var result = JsonConvert.DeserializeObject<ScryfallCollectionResult>(json);
                        if (result?.Data != null)
                            all.AddRange(result.Data);
                    }
                }
                catch { /* this chunk failed — caller falls back to per-name lookups */ }

                if (offset + 75 < list.Count)
                    await Task.Delay(100); // Scryfall rate limit between API requests
            }

            return all;
        }

        /// <summary>Fetch a single card by Scryfall ID.</summary>
        public async Task<ScryfallCard?> GetCardByIdAsync(string scryfallId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"https://api.scryfall.com/cards/{scryfallId}");
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<ScryfallCard>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the token cards related to the given card (via Scryfall's all_parts),
        /// with their artwork already downloaded and cached. Empty if the card makes no tokens.
        /// </summary>
        public async Task<List<(ScryfallCard Token, string ArtworkPath)>> GetTokensForCardAsync(string scryfallId)
        {
            var tokens = new List<(ScryfallCard, string)>();
            try
            {
                var card = await GetCardByIdAsync(scryfallId);
                var parts = card?.AllParts?.Where(p =>
                    string.Equals(p.Component, "token", StringComparison.OrdinalIgnoreCase)).ToList();
                if (parts == null || parts.Count == 0) return tokens;

                foreach (var part in parts)
                {
                    await Task.Delay(100); // Scryfall rate limit
                    var tokenCard = await GetCardByIdAsync(part.Id);
                    if (tokenCard == null) continue;

                    var path = await DownloadAndCacheImageAsync(tokenCard);
                    if (path != null)
                        tokens.Add((tokenCard, path));
                }
            }
            catch
            {
                // network/parse failure — return whatever we managed to gather
            }
            return tokens;
        }

        /// <summary>
        /// Like GetTokensForCardAsync, but starts from a card name (used for cards that
        /// have no Scryfall id, e.g. MPCFill imports): resolves the name to a Scryfall
        /// card, then returns its associated tokens with art downloaded.
        /// </summary>
        public async Task<List<(ScryfallCard Token, string ArtworkPath)>> GetTokensForCardByNameAsync(string cardName)
        {
            var card = await GetCardByNameAsync(cardName);
            if (card == null || string.IsNullOrEmpty(card.Id))
                return new List<(ScryfallCard, string)>();
            return await GetTokensForCardAsync(card.Id);
        }

        /// <summary>Fetch a card by name (exact match).</summary>
        public async Task<ScryfallCard?> GetCardByNameAsync(string cardName)
        {
            try
            {
                string encoded = System.Net.WebUtility.UrlEncode(cardName);
                var response = await _httpClient.GetAsync(
                    $"https://api.scryfall.com/cards/named?fuzzy={encoded}");
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<ScryfallCard>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
