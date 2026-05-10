using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MTGProxyBuilder.Core.Models
{
    public class BackArtEntry : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString();
        private string _name = string.Empty;
        private string _filePath = string.Empty;
        private string _source = string.Empty;
        private DateTime _addedDate = DateTime.Now;

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

        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); }
        }

        /// <summary>The contributor/source this art came from (e.g. MPCFill source name, or "Local" for user files).</summary>
        public string Source
        {
            get => _source;
            set { _source = value; OnPropertyChanged(); }
        }

        public DateTime AddedDate
        {
            get => _addedDate;
            set { _addedDate = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
