using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.UI.Controls
{
    public partial class LayerListPanel : UserControl
    {
        public LayerListPanel()
        {
            InitializeComponent();
        }

        // --- Plain events (replacing WPF RoutedEvents) ---

        public event EventHandler? AddImageLayerRequested;
        public event EventHandler? AddTextLayerRequested;
        public event EventHandler? DeleteLayerRequested;
        public event EventHandler? MoveUpRequested;
        public event EventHandler? MoveDownRequested;

        private void AddImageLayer_Click(object? sender, RoutedEventArgs e)
            => AddImageLayerRequested?.Invoke(this, EventArgs.Empty);

        private void AddTextLayer_Click(object? sender, RoutedEventArgs e)
            => AddTextLayerRequested?.Invoke(this, EventArgs.Empty);

        private void DeleteLayer_Click(object? sender, RoutedEventArgs e)
            => DeleteLayerRequested?.Invoke(this, EventArgs.Empty);

        private void MoveUp_Click(object? sender, RoutedEventArgs e)
            => MoveUpRequested?.Invoke(this, EventArgs.Empty);

        private void MoveDown_Click(object? sender, RoutedEventArgs e)
            => MoveDownRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Converts a LayerBase instance to its type icon string.
    /// </summary>
    public class LayerTypeConverter : IValueConverter
    {
        public static readonly LayerTypeConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
        {
            return value switch
            {
                ImageLayer => "Img",
                TextLayer => "T",
                _ => "?"
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
            => throw new NotSupportedException();
    }
}
