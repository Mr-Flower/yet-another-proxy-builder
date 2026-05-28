using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MTGProxyBuilder.Core.Models;

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
            var allFonts = new List<string>
            {
                "Arial",
                "Segoe UI",
                "Times New Roman",
                "Verdana",
                "Tahoma",
                "Georgia",
                "Courier New",
                "Trebuchet MS",
                "Impact",
                "Comic Sans MS"
            };
            FontComboBox.ItemsSource = allFonts;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            // DataContext changed — visibility is driven by UpdateForSelection calls
        }

        public void UpdateForSelection(LayerBase? layer)
        {
            if (layer == null)
            {
                NoSelectionText.IsVisible = true;
                PropertiesPanel.IsVisible = false;
            }
            else
            {
                NoSelectionText.IsVisible = false;
                PropertiesPanel.IsVisible = true;
                ImageProperties.IsVisible = layer is ImageLayer;
                TextProperties.IsVisible = layer is TextLayer;
            }
        }

        // --- Plain event (replacing WPF RoutedEvent) ---

        public event EventHandler? BrowseImageRequested;

        private void BrowseImage_Click(object? sender, RoutedEventArgs e)
            => BrowseImageRequested?.Invoke(this, EventArgs.Empty);
    }
}
