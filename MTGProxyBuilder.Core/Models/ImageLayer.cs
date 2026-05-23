using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Models
{
    public class ImageLayer : LayerBase
    {
        private string _imageSource = string.Empty;
        private bool _maskEnabled;
        private string? _maskPath;

        /// <summary>
        /// File path or embedded resource key for the image.
        /// </summary>
        public string ImageSource
        {
            get => _imageSource;
            set { _imageSource = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Cached raw image bytes. Not serialized — loaded lazily at runtime.
        /// </summary>
        [JsonIgnore]
        public byte[]? ImageBytes { get; set; }

        public bool MaskEnabled
        {
            get => _maskEnabled;
            set { _maskEnabled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Serializable mask definition (SVG path data or similar).
        /// Mask editing deferred to Phase 2.
        /// </summary>
        public string? MaskPath
        {
            get => _maskPath;
            set { _maskPath = value; OnPropertyChanged(); }
        }
    }
}
