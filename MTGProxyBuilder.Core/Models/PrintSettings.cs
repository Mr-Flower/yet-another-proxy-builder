using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MTGProxyBuilder.Core.Models
{
    public enum PrintMode
    {
        Duplex,
        FrontsOnly,
        BacksOnly
    }

    public enum OutlineAlignment
    {
        Center,
        Inside,
        Outside
    }

    public enum OutlineType
    {
        Full,
        Corners
    }

    public enum LineType
    {
        Solid,
        Dashed
    }

    public class PrintSettings : INotifyPropertyChanged
    {
        private PrintMode _printMode = PrintMode.Duplex;
        private int _dpi = Constants.DefaultDpi;
        private bool _showCutGuides = true;

        // Card outline guides
        private bool _showCardOutline = true;
        private string _outlineColor = "#66FF00";
        private OutlineAlignment _outlineAlignment = OutlineAlignment.Outside;
        private float _cornerRadiusMm = 3f;
        private OutlineType _outlineType = OutlineType.Corners;
        private LineType _outlineLineType = LineType.Solid;
        private float _cornerLengthMm = 5f;
        private float _lineWeight = 2f;

        // Silhouette Cameo
        private bool _showRegistrationMarks;
        private bool _exportSvgCutLines;
        private float _regMarkLengthIn = 0.35f;
        private float _regMarkThicknessIn = 0.039f;
        private float _regMarkInsetIn = 0.394f;

        public PrintMode PrintMode
        {
            get => _printMode;
            set { _printMode = value; OnPropertyChanged(); }
        }

        public int DPI
        {
            get => _dpi;
            set { _dpi = value; OnPropertyChanged(); }
        }

        public bool ShowCutGuides
        {
            get => _showCutGuides;
            set { _showCutGuides = value; OnPropertyChanged(); }
        }

        // --- Card Outline Guides ---

        public bool ShowCardOutline
        {
            get => _showCardOutline;
            set { _showCardOutline = value; OnPropertyChanged(); }
        }

        /// <summary>Hex color for the card outline, e.g. "#66FF00"</summary>
        public string OutlineColor
        {
            get => _outlineColor;
            set { _outlineColor = value; OnPropertyChanged(); }
        }

        public OutlineAlignment OutlineAlignment
        {
            get => _outlineAlignment;
            set { _outlineAlignment = value; OnPropertyChanged(); }
        }

        /// <summary>Corner radius in mm. 0 = sharp corners.</summary>
        public float CornerRadiusMm
        {
            get => _cornerRadiusMm;
            set { _cornerRadiusMm = value; OnPropertyChanged(); }
        }

        public OutlineType OutlineType
        {
            get => _outlineType;
            set { _outlineType = value; OnPropertyChanged(); }
        }

        public LineType OutlineLineType
        {
            get => _outlineLineType;
            set { _outlineLineType = value; OnPropertyChanged(); }
        }

        /// <summary>Length of corner marks in mm (only used when OutlineType = Corners).</summary>
        public float CornerLengthMm
        {
            get => _cornerLengthMm;
            set { _cornerLengthMm = value; OnPropertyChanged(); }
        }

        /// <summary>Line weight in points for the outline.</summary>
        public float LineWeight
        {
            get => _lineWeight;
            set { _lineWeight = value; OnPropertyChanged(); }
        }

        // --- Silhouette Cameo ---

        public bool ShowRegistrationMarks
        {
            get => _showRegistrationMarks;
            set { _showRegistrationMarks = value; OnPropertyChanged(); }
        }

        public bool ExportSvgCutLines
        {
            get => _exportSvgCutLines;
            set { _exportSvgCutLines = value; OnPropertyChanged(); }
        }

        /// <summary>Length of each registration mark arm in inches (default 0.35").</summary>
        public float RegMarkLengthIn
        {
            get => _regMarkLengthIn;
            set { _regMarkLengthIn = value; OnPropertyChanged(); }
        }

        /// <summary>Thickness of each registration mark arm in inches (default 0.039").</summary>
        public float RegMarkThicknessIn
        {
            get => _regMarkThicknessIn;
            set { _regMarkThicknessIn = value; OnPropertyChanged(); }
        }

        /// <summary>Distance from page edge to the registration mark corner in inches (default 0.394").</summary>
        public float RegMarkInsetIn
        {
            get => _regMarkInsetIn;
            set { _regMarkInsetIn = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
