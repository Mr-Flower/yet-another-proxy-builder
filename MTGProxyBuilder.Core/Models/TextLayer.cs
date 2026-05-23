namespace MTGProxyBuilder.Core.Models
{
    public class TextLayer : LayerBase
    {
        private string _text = string.Empty;
        private string _fontFamily = "Arial";
        private float _fontSize = 24f;
        private string _fontColor = "#FFFFFF";
        private bool _isBold;
        private bool _isItalic;
        private CardTextAlignment _textAlignment = CardTextAlignment.Left;
        private float _lineSpacing = 1.2f;
        private string? _strokeColor;
        private float _strokeWidth;

        public string Text
        {
            get => _text;
            set { _text = value; OnPropertyChanged(); }
        }

        public string FontFamily
        {
            get => _fontFamily;
            set { _fontFamily = value; OnPropertyChanged(); }
        }

        public float FontSize
        {
            get => _fontSize;
            set { _fontSize = value; OnPropertyChanged(); }
        }

        /// <summary>Hex color string, e.g. "#FFFFFF".</summary>
        public string FontColor
        {
            get => _fontColor;
            set { _fontColor = value; OnPropertyChanged(); }
        }

        public bool IsBold
        {
            get => _isBold;
            set { _isBold = value; OnPropertyChanged(); }
        }

        public bool IsItalic
        {
            get => _isItalic;
            set { _isItalic = value; OnPropertyChanged(); }
        }

        public CardTextAlignment TextAlignment
        {
            get => _textAlignment;
            set { _textAlignment = value; OnPropertyChanged(); }
        }

        public float LineSpacing
        {
            get => _lineSpacing;
            set { _lineSpacing = value; OnPropertyChanged(); }
        }

        /// <summary>Optional text stroke/outline color (hex). Null = no stroke.</summary>
        public string? StrokeColor
        {
            get => _strokeColor;
            set { _strokeColor = value; OnPropertyChanged(); }
        }

        public float StrokeWidth
        {
            get => _strokeWidth;
            set { _strokeWidth = value; OnPropertyChanged(); }
        }
    }
}
