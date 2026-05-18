using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class MpcSourceManagerDialog : Window
    {
        private readonly MpcFillSourceManager _manager;
        private readonly MpcFillService? _mpcFillService;
        private List<MpcFillSource> _allSources;
        private bool _initialized;

        public MpcSourceManagerDialog(MpcFillSourceManager manager, MpcFillService? mpcFillService = null)
        {
            _manager = manager;
            _mpcFillService = mpcFillService;
            _allSources = manager.AllSources.ToList();
            InitializeComponent();
            _initialized = true;

            // Always load on open — if sources are already cached this is near-instant
            Loaded += async (_, _) => await LoadSourcesAsync(forceReload: _allSources.Count == 0);
        }

        private async System.Threading.Tasks.Task LoadSourcesAsync(bool forceReload = false)
        {
            if (_mpcFillService == null)
            {
                // No service available — just show what we have
                RefreshList();
                if (_allSources.Count == 0)
                    CountLabel.Text = "No sources available (service unavailable)";
                return;
            }

            RefreshBtn.IsEnabled = false;
            CountLabel.Text = "Loading sources from MPCFill...";
            SummaryLabel.Text = "Connecting to mpcfill.com...";

            try
            {
                var error = await _mpcFillService.EnsureSourcesLoadedAsync(forceReload);
                _allSources = _manager.AllSources.ToList();

                if (error != null)
                {
                    CountLabel.Text = $"0 sources — failed to load";
                    SummaryLabel.Text = $"Error: {error}";
                    return;
                }

                if (_allSources.Count == 0)
                {
                    CountLabel.Text = "0 sources available";
                    SummaryLabel.Text = "MPCFill returned no sources. Click Refresh to retry.";
                    return;
                }

                RefreshList();
            }
            catch (Exception ex)
            {
                CountLabel.Text = "0 sources — failed to load";
                SummaryLabel.Text = $"Error: {ex.Message}";
            }
            finally
            {
                RefreshBtn.IsEnabled = true;
            }
        }

        private async void OnRefresh(object sender, RoutedEventArgs e)
        {
            await LoadSourcesAsync(forceReload: true);
        }

        private void RefreshList()
        {
            if (!_initialized) return;

            string filter = FilterBox?.Text?.Trim() ?? "";
            bool favsOnly = ShowFavs?.IsChecked == true;

            var filtered = _allSources.AsEnumerable();

            if (favsOnly)
                filtered = filtered.Where(s => s.IsFavorite);

            if (!string.IsNullOrEmpty(filter))
                filtered = filtered.Where(s =>
                    s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    s.Description.Contains(filter, StringComparison.OrdinalIgnoreCase));

            // Favorites first, then alphabetical
            var sorted = filtered
                .OrderByDescending(s => s.IsFavorite)
                .ThenBy(s => s.Name)
                .ToList();

            SourceList.ItemsSource = sorted;

            int totalFavs = _allSources.Count(s => s.IsFavorite);
            CountLabel.Text = $"{_allSources.Count} sources available";
            SummaryLabel.Text = $"{totalFavs} favorite(s) — " +
                (totalFavs > 0
                    ? "check \"Favorites only\" in the search panel to use them"
                    : "right-click results or use this dialog to add favorites");
        }

        private void OnFilterChanged(object sender, object e) => RefreshList();

        private void OnToggleFavorite(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int pk)
            {
                _manager.ToggleFavorite(pk);
                // Update the source object in our list
                var src = _allSources.FirstOrDefault(s => s.Pk == pk);
                if (src != null) src.IsFavorite = _manager.IsFavorite(pk);
                RefreshList();
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }

    // --- Simple value converters for the source list ---

    public class FavStarConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is true ? "\u2605" : "\u2606"; // ★ vs ☆
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    public class FavBrushConverter : IValueConverter
    {
        private static readonly Brush Gold = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
        private static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is true ? Gold : Gray;
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    public class FavActionConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is true ? "Unfavorite" : "Favorite";
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }
}
