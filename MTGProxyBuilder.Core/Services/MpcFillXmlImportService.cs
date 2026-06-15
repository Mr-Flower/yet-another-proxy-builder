using System.Xml.Linq;
using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Core.Services
{
    public class MpcFillXmlCard
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
        public string SourceType { get; set; } = "Google Drive";
        public List<int> Slots { get; set; } = new();
    }

    public class MpcFillXmlProject
    {
        public int Quantity { get; set; }
        public string Stock { get; set; } = string.Empty;
        public bool Foil { get; set; }
        public List<MpcFillXmlCard> Fronts { get; set; } = new();
        public List<MpcFillXmlCard> Backs { get; set; } = new();
        public string? CommonCardbackId { get; set; }
    }

    public class MpcFillXmlImportService
    {
        private readonly MpcFillService _mpcFill;
        private readonly ImageCacheService _imageCache;

        public MpcFillXmlImportService(MpcFillService mpcFill, ImageCacheService imageCache)
        {
            _mpcFill = mpcFill;
            _imageCache = imageCache;
        }

        /// <summary>Parse an MPCFill cards.xml file.</summary>
        public (MpcFillXmlProject? Project, string? Error) ParseXml(string filePath)
        {
            try
            {
                var doc = XDocument.Load(filePath);
                var root = doc.Root;
                if (root == null || root.Name.LocalName != "order")
                    return (null, "Invalid XML: root element must be <order>");

                var project = new MpcFillXmlProject();

                // Details
                var details = root.Element("details");
                if (details != null)
                {
                    project.Quantity = int.TryParse(details.Element("quantity")?.Value, out var q) ? q : 0;
                    project.Stock = details.Element("stock")?.Value ?? "";
                    project.Foil = details.Element("foil")?.Value?.ToLower() == "true";
                }

                // Fronts
                var fronts = root.Element("fronts");
                if (fronts != null)
                {
                    foreach (var cardEl in fronts.Elements("card"))
                        project.Fronts.Add(ParseCard(cardEl));
                }

                // Backs
                var backs = root.Element("backs");
                if (backs != null)
                {
                    foreach (var cardEl in backs.Elements("card"))
                        project.Backs.Add(ParseCard(cardEl));
                }

                // Common cardback
                project.CommonCardbackId = root.Element("cardback")?.Value;

                return (project, null);
            }
            catch (Exception ex)
            {
                return (null, $"Failed to parse XML: {ex.Message}");
            }
        }

        private static MpcFillXmlCard ParseCard(XElement el)
        {
            var card = new MpcFillXmlCard
            {
                Id = el.Element("id")?.Value ?? string.Empty,
                Name = el.Element("name")?.Value ?? string.Empty,
                Query = el.Element("query")?.Value ?? string.Empty,
                SourceType = el.Element("sourceType")?.Value ?? "Google Drive"
            };

            var slotsStr = el.Element("slots")?.Value ?? "";
            if (!string.IsNullOrEmpty(slotsStr))
            {
                card.Slots = slotsStr.Split(',')
                    .Select(s => int.TryParse(s.Trim(), out var n) ? n : -1)
                    .Where(n => n >= 0)
                    .ToList();
            }

            return card;
        }

        /// <summary>
        /// Extracts a clean card name from the MPCFill name field.
        /// Strips file extension and common suffixes like set codes in parentheses.
        /// </summary>
        public static string CleanCardName(MpcFillXmlCard card)
        {
            // Prefer the query field (it's the original search term)
            string name = !string.IsNullOrEmpty(card.Query)
                ? card.Query
                : card.Name;

            // Strip "t:" or "b:" prefixes
            if (name.StartsWith("t:") || name.StartsWith("b:"))
                name = name[2..];

            // Strip file extension
            var ext = Path.GetExtension(name);
            if (!string.IsNullOrEmpty(ext))
                name = Path.GetFileNameWithoutExtension(name);

            // Strip trailing parenthetical (set info) like " (LEA 161)"
            int parenIdx = name.LastIndexOf('(');
            if (parenIdx > 0)
                name = name[..parenIdx];

            return name.Trim();
        }

        /// <summary>
        /// Downloads a card image by its MPCFill identifier (Google Drive file ID).
        /// </summary>
        public async Task<string?> DownloadImageByIdAsync(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;

            var cached = _imageCache.GetCachedImagePath($"mpc_{identifier}");
            if (cached != null) return cached;

            // Google Drive download URL
            string url = $"https://drive.google.com/uc?id={identifier}&export=download";

            // Shared pool; disposing this client is cheap and leaves the pool intact.
            using var http = SharedHttp.CreateClient();

            return await _imageCache.CacheImageFromUrlAsync(http, url, $"mpc_{identifier}");
        }
    }
}
