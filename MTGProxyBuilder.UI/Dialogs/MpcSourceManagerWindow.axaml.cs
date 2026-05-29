using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Media;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.Dialogs;

public partial class MpcSourceManagerWindow : Window
{
    private readonly MpcFillSourceManager _manager;
    private readonly MpcFillService? _mpcFillService;
    private List<MpcFillSource> _allSources;
    private bool _initialized;

    public MpcSourceManagerWindow(MpcFillSourceManager manager, MpcFillService? mpcFillService = null)
    {
        _manager = manager;
        _mpcFillService = mpcFillService;
        _allSources = manager.AllSources.ToList();
        InitializeComponent();
        _initialized = true;

        ShowFavs.IsCheckedChanged += (_, _) => RefreshList();
        FilterBox.TextChanged += (_, _) => RefreshList();

        Loaded += async (_, _) => await LoadSourcesAsync(forceReload: _allSources.Count == 0);
    }

    private async Task LoadSourcesAsync(bool forceReload = false)
    {
        if (_mpcFillService == null)
        {
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
                CountLabel.Text = "0 sources — failed to load";
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

    private async void OnRefresh(object? sender, RoutedEventArgs e)
        => await LoadSourcesAsync(forceReload: true);

    private void RefreshList()
    {
        if (!_initialized) return;

        string filter = FilterBox?.Text?.Trim() ?? "";
        bool favsOnly = ShowFavs?.IsChecked == true;

        var filtered = _allSources.AsEnumerable();
        if (favsOnly) filtered = filtered.Where(s => s.IsFavorite);
        if (!string.IsNullOrEmpty(filter))
            filtered = filtered.Where(s =>
                s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                s.Description.Contains(filter, StringComparison.OrdinalIgnoreCase));

        var sorted = filtered
            .OrderByDescending(s => s.IsFavorite)
            .ThenBy(s => s.Name)
            .ToList();

        SourceList.ItemsSource = sorted;

        int totalFavs = _allSources.Count(s => s.IsFavorite);
        CountLabel.Text = $"{_allSources.Count} sources available";
        SummaryLabel.Text = $"{totalFavs} favorite(s) — " +
            (totalFavs > 0
                ? "check \"Favorites only\" in search panel to use them"
                : "click ☆ to add favorites");
    }

    private void OnFilterChanged(object? sender, RoutedEventArgs e) => RefreshList();

    private void OnToggleFavorite(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int pk)
        {
            _manager.ToggleFavorite(pk);
            var src = _allSources.FirstOrDefault(s => s.Pk == pk);
            if (src != null) src.IsFavorite = _manager.IsFavorite(pk);
            RefreshList();
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}

// ================================================================
//  VALUE CONVERTERS
// ================================================================

public class FavStarConverter : IValueConverter
{
    public static readonly FavStarConverter Instance = new();
    public object? Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? "★" : "☆";
    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

public class FavBrushConverter : IValueConverter
{
    public static readonly FavBrushConverter Instance = new();
    private static readonly IBrush Gold = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
    private static readonly IBrush Gray = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
    public object? Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? Gold : Gray;
    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
