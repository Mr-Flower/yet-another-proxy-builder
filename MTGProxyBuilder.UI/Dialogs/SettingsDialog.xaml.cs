using System.Windows;
using System.Windows.Controls;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class SettingsDialog : Window
    {
        private readonly AppSettingsService _settingsService;

        public SettingsDialog(AppSettingsService settingsService)
        {
            InitializeComponent();
            _settingsService = settingsService;

            var s = settingsService.Settings;
            TokenTextBox.Text = s.DefaultTokenText;
            BleedBox.Text = s.DefaultBleedMm.ToString();
            UpdateCheckBox.IsChecked = s.CheckForUpdates;

            // Select the matching page preset
            foreach (ComboBoxItem item in PagePresetBox.Items)
            {
                if (item.Content.ToString() == s.DefaultPagePreset)
                {
                    PagePresetBox.SelectedItem = item;
                    break;
                }
            }
            if (PagePresetBox.SelectedItem == null)
                PagePresetBox.SelectedIndex = 0;
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            var s = _settingsService.Settings;
            s.DefaultTokenText = TokenTextBox.Text;
            s.DefaultPagePreset = (PagePresetBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "A4";
            s.CheckForUpdates = UpdateCheckBox.IsChecked == true;

            if (float.TryParse(BleedBox.Text, out var bleed))
                s.DefaultBleedMm = bleed;

            _settingsService.Save();
            DialogResult = true;
        }
    }
}
