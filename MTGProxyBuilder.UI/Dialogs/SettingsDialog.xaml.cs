using System.Windows;
using System.Windows.Controls;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class SettingsDialog : Window
    {
        private readonly AppSettingsService _settingsService;
        private readonly MpcFillSourceManager _sourceManager;

        public SettingsDialog(AppSettingsService settingsService, MpcFillSourceManager sourceManager)
        {
            InitializeComponent();
            _settingsService = settingsService;
            _sourceManager = sourceManager;

            var s = settingsService.Settings;
            TokenTextBox.Text = s.DefaultTokenText;
            BleedBox.Text = s.DefaultBleedMm.ToString();
            UpdateCheckBox.IsChecked = s.CheckForUpdates;
            UseFavoritesCheckBox.IsChecked = s.MpcFillUseFavoritesOnly;

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

            UpdateFavoritesInfo();
        }

        private void UpdateFavoritesInfo()
        {
            int favCount = _sourceManager.FavoritePks.Count;
            if (_sourceManager.IsLoaded)
            {
                FavoritesInfoLabel.Text = favCount > 0
                    ? $"{favCount} favorite source(s) selected out of {_sourceManager.AllSources.Count}"
                    : $"{_sourceManager.AllSources.Count} sources available — no favorites set (all sources will be used)";
            }
            else
            {
                FavoritesInfoLabel.Text = favCount > 0
                    ? $"{favCount} favorite source(s) saved"
                    : "No favorites set (all sources will be used)";
            }
        }

        private void OnManageSources(object sender, RoutedEventArgs e)
        {
            var dialog = new MpcSourceManagerDialog(_sourceManager);
            dialog.Owner = this;
            dialog.ShowDialog();
            UpdateFavoritesInfo();
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            var s = _settingsService.Settings;
            s.DefaultTokenText = TokenTextBox.Text;
            s.DefaultPagePreset = (PagePresetBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "A4";
            s.CheckForUpdates = UpdateCheckBox.IsChecked == true;
            s.MpcFillUseFavoritesOnly = UseFavoritesCheckBox.IsChecked == true;

            if (float.TryParse(BleedBox.Text, out var bleed))
                s.DefaultBleedMm = bleed;

            _settingsService.Save();
            DialogResult = true;
        }
    }
}
