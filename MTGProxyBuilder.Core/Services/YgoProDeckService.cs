using MTGProxyBuilder.Core.Models;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Services
{
    /// <summary>Response shape of the YGOPRODeck /cardinfo.php endpoint.</summary>
    public class YgoProDeckSearchResult
    {
        [JsonProperty("data")]
        public List<YgoProDeckCard>? Data { get; set; }

        /// <summary>Set by the API (with HTTP 400) when no card matched the query.</summary>
        [JsonProperty("error")]
        public string? Error { get; set; }
    }

    /// <summary>One artwork of a YGOPRODeck card. A card may carry several (alternate arts).</summary>
    public class YgoProDeckCardImage
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("image_url")]
        public string? ImageUrl { get; set; }

        [JsonProperty("image_url_small")]
        public string? ImageUrlSmall { get; set; }

        [JsonProperty("image_url_cropped")]
        public string? ImageUrlCropped { get; set; }
    }

    public class YgoProDeckCard
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>e.g. "Effect Monster", "Spell Card", "Synchro Monster".</summary>
        [JsonProperty("type")]
        public string? Type { get; set; }

        [JsonProperty("desc")]
        public string? Desc { get; set; }

        /// <summary>Monster type / spell-trap property, e.g. "Spellcaster", "Continuous".</summary>
        [JsonProperty("race")]
        public string? Race { get; set; }

        /// <summary>Monster attribute, e.g. "DARK" (null for spells/traps).</summary>
        [JsonProperty("attribute")]
        public string? Attribute { get; set; }

        [JsonProperty("archetype")]
        public string? Archetype { get; set; }

        [JsonProperty("atk")]
        public int? Atk { get; set; }

        [JsonProperty("def")]
        public int? Def { get; set; }

        [JsonProperty("level")]
        public int? Level { get; set; }

        [JsonProperty("card_images")]
        public List<YgoProDeckCardImage>? CardImages { get; set; }

        /// <summary>Full-resolution artwork URL for the given printing (defaults to the first).</summary>
        public string? GetImageUrl(int index = 0)
            => CardImages != null && index >= 0 && index < CardImages.Count
                ? CardImages[index].ImageUrl
                : null;

        /// <summary>Small thumbnail URL of the first artwork, for binding in result lists.</summary>
        [JsonIgnore]
        public string? SmallImageUrl => CardImages?.FirstOrDefault()?.ImageUrlSmall;

        /// <summary>Populate a CardModel from this card, using the already-downloaded artwork path.
        /// Yu-Gi-Oh! cards are single-faced, so the standard card back is applied separately.</summary>
        public CardModel ToCardModel(string artworkPath, int imageIndex = 0)
        {
            return new CardModel
            {
                Name = Name,
                ArtworkPath = artworkPath,
                // Full-res URL for on-demand download at PDF export (ArtworkPath holds a cached copy).
                FullResFrontUrl = GetImageUrl(imageIndex),
                IncludeBack = false,
                TypeLine = BuildTypeLine(),
                OracleText = Desc ?? string.Empty,
                // Map Yu-Gi-Oh! ATK/DEF onto the existing power/toughness fields for display.
                Power = Atk?.ToString() ?? string.Empty,
                Toughness = Def?.ToString() ?? string.Empty,
                Keywords = Archetype ?? string.Empty,
                DateAdded = DateTime.Now,
                Source = CardSource.YgoProDeck
            };
        }

        /// <summary>A readable type line combining card type, attribute and monster race.</summary>
        private string BuildTypeLine()
        {
            var parts = new[] { Type, Attribute, Race }.Where(p => !string.IsNullOrWhiteSpace(p));
            return string.Join(" · ", parts);
        }

        public override string ToString() => $"{Name} ({Type})";
    }

    /// <summary>
    /// Fetches Yu-Gi-Oh! cards from the free YGOPRODeck API (db.ygoprodeck.com), mirroring
    /// <see cref="ScryfallService"/>. Card images are downloaded into the shared image cache
    /// (YGOPRODeck asks callers to host images locally rather than hotlink repeatedly).
    /// </summary>
    public class YgoProDeckService
    {
        private const string CardInfoEndpoint = "https://db.ygoprodeck.com/api/v7/cardinfo.php";

        private readonly HttpClient _httpClient;
        private readonly ImageCacheService _imageCache;

        public YgoProDeckService(ImageCacheService imageCache)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MTGProxyBuilder/1.0");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _imageCache = imageCache;
        }

        /// <summary>
        /// Searches Yu-Gi-Oh! cards by name. Fuzzy (default) does a partial-name match (fname=),
        /// exact uses name=. A "no card found" response is returned as an empty list, not an error.
        /// </summary>
        public async Task<(List<YgoProDeckCard> Cards, string? Error)> SearchCardAsync(
            string cardName, bool fuzzy = true)
        {
            if (string.IsNullOrWhiteSpace(cardName)) return (new(), null);

            try
            {
                string param = fuzzy ? "fname" : "name";
                string url = $"{CardInfoEndpoint}?{param}={System.Net.WebUtility.UrlEncode(cardName)}";
                var response = await _httpClient.GetAsync(url);
                string content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return (new(), ErrorFromFailedResponse(response.StatusCode, content));

                var result = JsonConvert.DeserializeObject<YgoProDeckSearchResult>(content);
                return (result?.Data ?? new(), null);
            }
            catch (HttpRequestException ex) { return (new(), $"Network error: {ex.Message}"); }
            catch (TaskCanceledException) { return (new(), "Request timed out"); }
            catch (Exception ex) { return (new(), $"Error: {ex.Message}"); }
        }

        /// <summary>A failed HTTP status is "no results" when YGOPRODeck reports no match (400),
        /// otherwise a real error worth surfacing.</summary>
        private static string? ErrorFromFailedResponse(System.Net.HttpStatusCode status, string body)
        {
            if (status == System.Net.HttpStatusCode.BadRequest) return null; // no card matched
            return $"YGOPRODeck returned {(int)status}: {body[..Math.Min(body.Length, 200)]}";
        }

        /// <summary>Cache key for one artwork of a card (shared by download + cache-probe).</summary>
        private static string ImageCacheKey(YgoProDeckCard card, int imageIndex)
        {
            long imageId = card.CardImages != null && imageIndex < card.CardImages.Count
                ? card.CardImages[imageIndex].Id
                : card.Id;
            return $"ygo_{imageId}";
        }

        /// <summary>Cached path for this card's artwork if already on disk, else null — lets callers
        /// show already-downloaded art instantly without re-downloading.</summary>
        public string? GetCachedImagePath(YgoProDeckCard card, int imageIndex = 0)
            => _imageCache.GetCachedImagePath(ImageCacheKey(card, imageIndex));

        /// <summary>Downloads (or returns the cached) artwork for the given printing of a card.</summary>
        public async Task<string?> DownloadAndCacheImageAsync(YgoProDeckCard card, int imageIndex = 0)
        {
            string cacheKey = ImageCacheKey(card, imageIndex);

            var cached = _imageCache.GetCachedImagePath(cacheKey);
            if (cached != null) return cached;

            string? imageUrl = card.GetImageUrl(imageIndex);
            if (imageUrl == null) return null;

            var path = await _imageCache.CacheImageFromUrlAsync(_httpClient, imageUrl, cacheKey);
            if (path != null) _imageCache.SetMetadata(cacheKey, card.Name, "YGOPRODeck");
            return path;
        }

        /// <summary>Downloads an arbitrary image URL into the shared cache under the given key,
        /// reusing this service's HttpClient. Used to fetch full-resolution art at PDF export.</summary>
        public async Task<string?> DownloadUrlToCacheAsync(string? url, string cacheKey)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var cached = _imageCache.GetCachedImagePath(cacheKey);
            if (cached != null) return cached;
            return await _imageCache.CacheImageFromUrlAsync(_httpClient, url, cacheKey);
        }
    }
}
