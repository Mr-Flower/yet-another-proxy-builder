using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Models
{
    public abstract class LayerBase : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString();
        private string _name = string.Empty;
        private bool _isVisible = true;
        private float _opacity = 1.0f;
        private int _zOrder;
        private float _x;
        private float _y;
        private float _width;
        private float _height;
        private float _rotation;
        private bool _isLocked;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(); }
        }

        public float Opacity
        {
            get => _opacity;
            set { _opacity = Math.Clamp(value, 0f, 1f); OnPropertyChanged(); }
        }

        public int ZOrder
        {
            get => _zOrder;
            set { _zOrder = value; OnPropertyChanged(); }
        }

        public float X
        {
            get => _x;
            set { _x = value; OnPropertyChanged(); }
        }

        public float Y
        {
            get => _y;
            set { _y = value; OnPropertyChanged(); }
        }

        public float Width
        {
            get => _width;
            set { _width = value; OnPropertyChanged(); }
        }

        public float Height
        {
            get => _height;
            set { _height = value; OnPropertyChanged(); }
        }

        public float Rotation
        {
            get => _rotation;
            set { _rotation = value; OnPropertyChanged(); }
        }

        public bool IsLocked
        {
            get => _isLocked;
            set { _isLocked = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
