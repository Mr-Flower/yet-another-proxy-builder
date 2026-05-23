using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MTGProxyBuilder.Core.Models
{
    public class CustomCardProject : INotifyPropertyChanged
    {
        private string _projectId = Guid.NewGuid().ToString();
        private string _projectName = "Untitled Card";
        private int _cardWidthPx = 744;   // 63mm at 300 DPI
        private int _cardHeightPx = 1039; // 88mm at 300 DPI
        private string _backgroundColor = "#000000";
        private List<LayerBase> _layers = new();
        private DateTime _createdDate = DateTime.Now;
        private DateTime _lastModified = DateTime.Now;

        public string ProjectId
        {
            get => _projectId;
            set { _projectId = value; OnPropertyChanged(); }
        }

        public string ProjectName
        {
            get => _projectName;
            set { _projectName = value; OnPropertyChanged(); }
        }

        /// <summary>Card width in pixels at 300 DPI. Default: 744 (63mm).</summary>
        public int CardWidthPx
        {
            get => _cardWidthPx;
            set { _cardWidthPx = value; OnPropertyChanged(); }
        }

        /// <summary>Card height in pixels at 300 DPI. Default: 1039 (88mm).</summary>
        public int CardHeightPx
        {
            get => _cardHeightPx;
            set { _cardHeightPx = value; OnPropertyChanged(); }
        }

        /// <summary>Background color as hex string, e.g. "#000000".</summary>
        public string BackgroundColor
        {
            get => _backgroundColor;
            set { _backgroundColor = value; OnPropertyChanged(); }
        }

        /// <summary>Layers ordered by ZOrder (lowest = bottom).</summary>
        public List<LayerBase> Layers
        {
            get => _layers;
            set { _layers = value; OnPropertyChanged(); }
        }

        public DateTime CreatedDate
        {
            get => _createdDate;
            set { _createdDate = value; OnPropertyChanged(); }
        }

        public DateTime LastModified
        {
            get => _lastModified;
            set { _lastModified = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
