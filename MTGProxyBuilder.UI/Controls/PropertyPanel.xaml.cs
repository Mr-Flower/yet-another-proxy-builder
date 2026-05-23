using System.Windows;
using System.Windows.Controls;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Resources;

namespace MTGProxyBuilder.UI.Controls
{
    public partial class PropertyPanel : UserControl
    {
        public PropertyPanel()
        {
            InitializeComponent();
            LoadFonts();
            DataContextChanged += OnDataContextChanged;
        }

        private void LoadFonts()
        {
            var fonts = FontProvider.GetAllFontNames();
            var allFonts = new List<string> { "Arial", "Segoe UI", "Times New Roman" };
            foreach (var f in fonts)
            {
                if (!allFonts.Contains(f))
                    allFonts.Add(f);
            }
            FontComboBox.ItemsSource = allFonts;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateVisibility();
        }

        public void UpdateForSelection(LayerBase? layer)
        {
            if (layer == null)
            {
                NoSelectionText.Visibility = Visibility.Visible;
                PropertiesPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                NoSelectionText.Visibility = Visibility.Collapsed;
                PropertiesPanel.Visibility = Visibility.Visible;
                ImageProperties.Visibility = layer is ImageLayer ? Visibility.Visible : Visibility.Collapsed;
                TextProperties.Visibility = layer is TextLayer ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateVisibility()
        {
            // Will be called by the ViewModel binding changes
        }

        // --- Routed Events ---

        public static readonly RoutedEvent BrowseImageRequestedEvent =
            EventManager.RegisterRoutedEvent("BrowseImageRequested", RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(PropertyPanel));

        public event RoutedEventHandler BrowseImageRequested
        {
            add => AddHandler(BrowseImageRequestedEvent, value);
            remove => RemoveHandler(BrowseImageRequestedEvent, value);
        }

        private void BrowseImage_Click(object sender, RoutedEventArgs e)
            => RaiseEvent(new RoutedEventArgs(BrowseImageRequestedEvent));
    }
}
