using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class ColorPickerDialog : Window
    {
        private bool _updatingFromCode;

        public string SelectedHexColor { get; private set; } = "#66FF00";

        private static readonly string[] PresetColors = new[]
        {
            // Greens (cutting guide defaults)
            "#66FF00", "#00FF00", "#00FF66", "#00CC00", "#33CC33",
            // Bright / high visibility
            "#FF0000", "#FF6600", "#FFFF00", "#00FFFF", "#FF00FF",
            "#FF3399", "#00CCFF", "#9933FF", "#FF9900", "#CCFF00",
            // Standard
            "#FFFFFF", "#000000", "#333333", "#666666", "#999999",
            "#CC0000", "#0000CC", "#006600", "#663300", "#330066",
            // Pastels
            "#FF9999", "#99FF99", "#9999FF", "#FFFF99", "#FF99FF",
            "#99FFFF", "#FFCC99", "#CC99FF", "#99FFCC", "#CCCCFF"
        };

        public ColorPickerDialog(string currentHex)
        {
            InitializeComponent();
            SelectedHexColor = currentHex;
            BuildSwatches();
            SetFromHex(currentHex);
        }

        private void BuildSwatches()
        {
            foreach (var hex in PresetColors)
            {
                var border = new Border
                {
                    Width = 28, Height = 28, Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(3), Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent
                };

                try
                {
                    border.Background = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(hex));
                }
                catch { border.Background = Brushes.Gray; }

                string capturedHex = hex;
                border.MouseLeftButtonUp += (_, _) => SetFromHex(capturedHex);
                border.ToolTip = hex;

                SwatchPanel.Children.Add(border);
            }
        }

        private void SetFromHex(string hex)
        {
            _updatingFromCode = true;
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length < 6) hex = hex.PadRight(6, '0');

                byte r = Convert.ToByte(hex[..2], 16);
                byte g = Convert.ToByte(hex[2..4], 16);
                byte b = Convert.ToByte(hex[4..6], 16);

                SliderR.Value = r;
                SliderG.Value = g;
                SliderB.Value = b;

                UpdatePreview();
            }
            catch { }
            finally { _updatingFromCode = false; }
        }

        private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingFromCode) return;
            UpdatePreview();
        }

        private void OnHexChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingFromCode) return;
            string hex = HexBox.Text.TrimStart('#');
            if (hex.Length == 6)
            {
                try
                {
                    _updatingFromCode = true;
                    byte r = Convert.ToByte(hex[..2], 16);
                    byte g = Convert.ToByte(hex[2..4], 16);
                    byte b = Convert.ToByte(hex[4..6], 16);
                    SliderR.Value = r;
                    SliderG.Value = g;
                    SliderB.Value = b;
                    UpdatePreview();
                }
                catch { }
                finally { _updatingFromCode = false; }
            }
        }

        private void UpdatePreview()
        {
            byte r = (byte)SliderR.Value;
            byte g = (byte)SliderG.Value;
            byte b = (byte)SliderB.Value;

            var color = Color.FromRgb(r, g, b);
            PreviewSwatch.Background = new SolidColorBrush(color);
            SelectedHexColor = $"#{r:X2}{g:X2}{b:X2}";

            LabelR.Text = r.ToString();
            LabelG.Text = g.ToString();
            LabelB.Text = b.ToString();

            _updatingFromCode = true;
            HexBox.Text = SelectedHexColor;
            _updatingFromCode = false;

            // Highlight matching swatch
            foreach (var child in SwatchPanel.Children)
            {
                if (child is Border bd)
                {
                    bd.BorderBrush = bd.ToolTip?.ToString() == SelectedHexColor
                        ? Brushes.White : Brushes.Transparent;
                }
            }
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
