using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.UI.Controls
{
    public partial class LayerListPanel : UserControl
    {
        public LayerListPanel()
        {
            InitializeComponent();
        }

        // --- Routed Events for parent to handle ---

        public static readonly RoutedEvent AddImageLayerRequestedEvent =
            EventManager.RegisterRoutedEvent("AddImageLayerRequested", RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(LayerListPanel));

        public event RoutedEventHandler AddImageLayerRequested
        {
            add => AddHandler(AddImageLayerRequestedEvent, value);
            remove => RemoveHandler(AddImageLayerRequestedEvent, value);
        }

        public static readonly RoutedEvent AddTextLayerRequestedEvent =
            EventManager.RegisterRoutedEvent("AddTextLayerRequested", RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(LayerListPanel));

        public event RoutedEventHandler AddTextLayerRequested
        {
            add => AddHandler(AddTextLayerRequestedEvent, value);
            remove => RemoveHandler(AddTextLayerRequestedEvent, value);
        }

        public static readonly RoutedEvent DeleteLayerRequestedEvent =
            EventManager.RegisterRoutedEvent("DeleteLayerRequested", RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(LayerListPanel));

        public event RoutedEventHandler DeleteLayerRequested
        {
            add => AddHandler(DeleteLayerRequestedEvent, value);
            remove => RemoveHandler(DeleteLayerRequestedEvent, value);
        }

        public static readonly RoutedEvent MoveUpRequestedEvent =
            EventManager.RegisterRoutedEvent("MoveUpRequested", RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(LayerListPanel));

        public event RoutedEventHandler MoveUpRequested
        {
            add => AddHandler(MoveUpRequestedEvent, value);
            remove => RemoveHandler(MoveUpRequestedEvent, value);
        }

        public static readonly RoutedEvent MoveDownRequestedEvent =
            EventManager.RegisterRoutedEvent("MoveDownRequested", RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(LayerListPanel));

        public event RoutedEventHandler MoveDownRequested
        {
            add => AddHandler(MoveDownRequestedEvent, value);
            remove => RemoveHandler(MoveDownRequestedEvent, value);
        }

        private void AddImageLayer_Click(object sender, RoutedEventArgs e)
            => RaiseEvent(new RoutedEventArgs(AddImageLayerRequestedEvent));

        private void AddTextLayer_Click(object sender, RoutedEventArgs e)
            => RaiseEvent(new RoutedEventArgs(AddTextLayerRequestedEvent));

        private void DeleteLayer_Click(object sender, RoutedEventArgs e)
            => RaiseEvent(new RoutedEventArgs(DeleteLayerRequestedEvent));

        private void MoveUp_Click(object sender, RoutedEventArgs e)
            => RaiseEvent(new RoutedEventArgs(MoveUpRequestedEvent));

        private void MoveDown_Click(object sender, RoutedEventArgs e)
            => RaiseEvent(new RoutedEventArgs(MoveDownRequestedEvent));
    }

    /// <summary>
    /// Converts a LayerBase instance to its type name string for DataTrigger matching.
    /// </summary>
    public class LayerTypeConverter : IValueConverter
    {
        public static readonly LayerTypeConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch
            {
                ImageLayer => "Image",
                TextLayer => "Text",
                _ => "Unknown"
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
