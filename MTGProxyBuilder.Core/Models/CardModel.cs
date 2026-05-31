using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MTGProxyBuilder.Core.Models
{
    public class CardModel : INotifyPropertyChanged
    {
        private string _cardId = Guid.NewGuid().ToString();
        private string _name = string.Empty;
        private string _artworkPath = string.Empty;
        private string? _backArtworkPath;
        private string? _originalBackArtworkPath;
        private string? _scryfallId;
        private int _quantity = 1;
        private bool _includeBack;

        // Scryfall metadata
        private string _manaCost = string.Empty;
        private float _cmc;
        private string _typeLine = string.Empty;
        private string _oracleText = string.Empty;
        private string _rarity = string.Empty;
        private string _colors = string.Empty;
        private string _colorIdentity = string.Empty;
        private string _setCode = string.Empty;
        private string _setName = string.Empty;
        private string _collectorNumber = string.Empty;
        private string _artist = string.Empty;
        private string _power = string.Empty;
        private string _toughness = string.Empty;
        private string _loyalty = string.Empty;
        private string _keywords = string.Empty;
        private DateTime _dateAdded = DateTime.Now;
        private string _overlayText = string.Empty;
        private CardSource _source = CardSource.Unknown; // fork-specific

        public string CardId
        {
            get => _cardId;
            set { _cardId = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string ArtworkPath
        {
            get => _artworkPath;
            set { _artworkPath = value; OnPropertyChanged(); }
        }

        public string? BackArtworkPath
        {
            get => _backArtworkPath;
            set { _backArtworkPath = value; OnPropertyChanged(); }
        }

        /// <summary>Stores the original back art path from Scryfall so it can be restored.</summary>
        public string? OriginalBackArtworkPath
        {
            get => _originalBackArtworkPath;
            set { _originalBackArtworkPath = value; OnPropertyChanged(); }
        }

        public string? ScryfallId
        {
            get => _scryfallId;
            set { _scryfallId = value; OnPropertyChanged(); }
        }

        public int Quantity
        {
            get => _quantity;
            set { _quantity = value; OnPropertyChanged(); }
        }

        public bool IncludeBack
        {
            get => _includeBack;
            set { _includeBack = value; OnPropertyChanged(); }
        }

        // --- Scryfall metadata ---

        public string ManaCost
        {
            get => _manaCost;
            set { _manaCost = value; OnPropertyChanged(); }
        }

        /// <summary>Converted mana cost (e.g. 3.0 for a {1}{U}{U} spell)</summary>
        public float CMC
        {
            get => _cmc;
            set { _cmc = value; OnPropertyChanged(); }
        }

        /// <summary>e.g. "Creature — Human Wizard"</summary>
        public string TypeLine
        {
            get => _typeLine;
            set { _typeLine = value; OnPropertyChanged(); }
        }

        public string OracleText
        {
            get => _oracleText;
            set { _oracleText = value; OnPropertyChanged(); }
        }

        /// <summary>"common", "uncommon", "rare", "mythic"</summary>
        public string Rarity
        {
            get => _rarity;
            set { _rarity = value; OnPropertyChanged(); }
        }

        /// <summary>Comma-separated: "W", "U", "B", "R", "G" or "" for colorless</summary>
        public string Colors
        {
            get => _colors;
            set { _colors = value; OnPropertyChanged(); }
        }

        public string ColorIdentity
        {
            get => _colorIdentity;
            set { _colorIdentity = value; OnPropertyChanged(); }
        }

        /// <summary>Three-letter set code, e.g. "MH2"</summary>
        public string SetCode
        {
            get => _setCode;
            set { _setCode = value; OnPropertyChanged(); }
        }

        /// <summary>Full set name, e.g. "Modern Horizons 2"</summary>
        public string SetName
        {
            get => _setName;
            set { _setName = value; OnPropertyChanged(); }
        }

        public string CollectorNumber
        {
            get => _collectorNumber;
            set { _collectorNumber = value; OnPropertyChanged(); }
        }

        public string Artist
        {
            get => _artist;
            set { _artist = value; OnPropertyChanged(); }
        }

        public string Power
        {
            get => _power;
            set { _power = value; OnPropertyChanged(); }
        }

        public string Toughness
        {
            get => _toughness;
            set { _toughness = value; OnPropertyChanged(); }
        }

        public string Loyalty
        {
            get => _loyalty;
            set { _loyalty = value; OnPropertyChanged(); }
        }

        /// <summary>Comma-separated keyword abilities</summary>
        public string Keywords
        {
            get => _keywords;
            set { _keywords = value; OnPropertyChanged(); }
        }

        public DateTime DateAdded
        {
            get => _dateAdded;
            set { _dateAdded = value; OnPropertyChanged(); }
        }

        /// <summary>Text overlay rendered on the card (e.g. "TOKEN"). Empty = no overlay.</summary>
        public string OverlayText
        {
            get => _overlayText;
            set { _overlayText = value; OnPropertyChanged(); }
        }

        /// <summary>Macro-source of the artwork (Scryfall / MPCFill). Fork-specific.</summary>
        public CardSource Source
        {
            get => _source;
            set { _source = value; OnPropertyChanged(); }
        }

        /// <summary>Helper: primary card type (Creature, Instant, Sorcery, etc.)</summary>
        public string PrimaryType =>
            TypeLine.Split("—")[0].Trim().Split(' ').LastOrDefault() ?? string.Empty;

        /// <summary>
        /// Deep copy of this card as a DISTINCT physical card (gets a fresh CardId).
        /// Used to split one copy off a stacked card so it can be edited independently.
        /// Quantity is copied as-is; callers typically set it to 1.
        /// </summary>
        public CardModel Clone() => new()
        {
            // CardId intentionally NOT copied — the field initializer assigns a new Guid.
            Name = Name,
            ArtworkPath = ArtworkPath,
            BackArtworkPath = BackArtworkPath,
            OriginalBackArtworkPath = OriginalBackArtworkPath,
            ScryfallId = ScryfallId,
            Quantity = Quantity,
            IncludeBack = IncludeBack,
            ManaCost = ManaCost,
            CMC = CMC,
            TypeLine = TypeLine,
            OracleText = OracleText,
            Rarity = Rarity,
            Colors = Colors,
            ColorIdentity = ColorIdentity,
            SetCode = SetCode,
            SetName = SetName,
            CollectorNumber = CollectorNumber,
            Artist = Artist,
            Power = Power,
            Toughness = Toughness,
            Loyalty = Loyalty,
            Keywords = Keywords,
            DateAdded = DateAdded,
            OverlayText = OverlayText,
            Source = Source
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
