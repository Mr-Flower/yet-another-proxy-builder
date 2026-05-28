using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.Services;

namespace MTGProxyBuilder.UI.Dialogs;

public partial class SettingsWindow : Window
{
    private readonly AppSettingsService _settingsService;
    private readonly MpcFillSourceManager _sourceManager;
    private readonly MpcFillService? _mpcFillService;
    private readonly AvaloniaDialogService _dialogService;

    public SettingsWindow(AppSettingsService settingsService, MpcFillSourceManager sourceManager,
        MpcFillService? mpcFillService, AvaloniaDialogService dialogService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _sourceManager = sourceManager;
        _mpcFillService = mpcFillService;
        _dialogService = dialogService;

        Loaded += (_, _) => PopulateFromSettings();
    }

    private void PopulateFromSettings()
    {
        var s = _settingsService.Settings;

        TokenTextBox.Text = s.DefaultTokenText;
        BleedBox.Text = s.DefaultBleedMm.ToString();
        UpdateCheckBox.IsChecked = s.CheckForUpdates;
        UseFavoritesCheckBox.IsChecked = s.MpcFillUseFavoritesOnly;

        SelectComboByContent(PagePresetBox, s.DefaultPagePreset);
        SelectComboByTag(SortByBox, s.MpcFillDefaultSortBy);
        SelectComboByTag(DefaultMinDpiBox, s.MpcFillDefaultMinDpi.ToString());
        SelectComboByTag(DefaultMaxDpiBox, s.MpcFillDefaultMaxDpi.ToString());
        MaxSizeBox.Text = s.MpcFillMaximumSize.ToString();
        DefaultFuzzySearchBox.IsChecked = s.MpcFillDefaultFuzzySearch;
        FilterCardbacksBox.IsChecked = s.MpcFillFilterCardbacks;

        CardTypeCard.IsChecked = s.MpcFillCardTypes.Contains("CARD");
        CardTypeToken.IsChecked = s.MpcFillCardTypes.Contains("TOKEN");
        CardTypeCardback.IsChecked = s.MpcFillCardTypes.Contains("CARDBACK");

        LangEN.IsChecked = s.MpcFillLanguages.Contains("EN");
        LangJA.IsChecked = s.MpcFillLanguages.Contains("JA");
        LangFR.IsChecked = s.MpcFillLanguages.Contains("FR");
        LangDE.IsChecked = s.MpcFillLanguages.Contains("DE");
        LangES.IsChecked = s.MpcFillLanguages.Contains("ES");
        LangIT.IsChecked = s.MpcFillLanguages.Contains("IT");
        LangPT.IsChecked = s.MpcFillLanguages.Contains("PT");
        LangZH.IsChecked = s.MpcFillLanguages.Contains("ZH");
        LangRU.IsChecked = s.MpcFillLanguages.Contains("RU");
        LangAR.IsChecked = s.MpcFillLanguages.Contains("AR");
        LangSA.IsChecked = s.MpcFillLanguages.Contains("SA");

        ExcludeNsfwBox.IsChecked = s.MpcFillExcludeNsfw;
        ExcludeAiArtBox.IsChecked = s.MpcFillExcludeAiArt;

        FrontLibPathBox.Text = s.FrontArtLibraryPath ?? "(default)";
        BackLibPathBox.Text = s.BackArtLibraryPath ?? "(default)";

        UpdateFavoritesInfo();
    }

    private void UpdateFavoritesInfo()
    {
        int favCount = _sourceManager.FavoritePks.Count;
        if (_sourceManager.IsLoaded)
        {
            FavoritesInfoLabel.Text = favCount > 0
                ? $"{favCount} favorite source(s) selected out of {_sourceManager.AllSources.Count}"
                : $"{_sourceManager.AllSources.Count} sources available — no favorites set (all sources used)";
        }
        else
        {
            FavoritesInfoLabel.Text = favCount > 0
                ? $"{favCount} favorite source(s) saved"
                : "No favorites set (all sources will be used)";
        }
    }

    private void OnNavChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs _)
    {
        if (PageGeneral == null) return;
        PageGeneral.IsVisible = NavGeneral.IsChecked == true;
        PageLibraries.IsVisible = NavLibraries.IsChecked == true;
        PageMpcFill.IsVisible = NavMpcFill.IsChecked == true;
        PageLanguages.IsVisible = NavLanguages.IsChecked == true;
        PageFilters.IsVisible = NavFilters.IsChecked == true;
    }

    private async void OnManageSources(object? sender, RoutedEventArgs e)
    {
        await _dialogService.ShowMpcSourceManagerAsync(_sourceManager, _mpcFillService!);
        UpdateFavoritesInfo();
    }

    private async void OnBrowseFrontLibPath(object? sender, RoutedEventArgs e)
    {
        var path = await _dialogService.PickFolderAsync("Select front art library folder");
        if (path != null) FrontLibPathBox.Text = path;
    }

    private async void OnBrowseBackLibPath(object? sender, RoutedEventArgs e)
    {
        var path = await _dialogService.PickFolderAsync("Select back art library folder");
        if (path != null) BackLibPathBox.Text = path;
    }

    private void OnResetFrontLibPath(object? sender, RoutedEventArgs e)
        => FrontLibPathBox.Text = "(default)";

    private void OnResetBackLibPath(object? sender, RoutedEventArgs e)
        => BackLibPathBox.Text = "(default)";

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var s = _settingsService.Settings;
        s.DefaultTokenText = TokenTextBox.Text ?? "";
        s.DefaultPagePreset = GetComboContent(PagePresetBox) ?? "A4";
        s.CheckForUpdates = UpdateCheckBox.IsChecked == true;
        s.MpcFillUseFavoritesOnly = UseFavoritesCheckBox.IsChecked == true;

        if (float.TryParse(BleedBox.Text, out var bleed)) s.DefaultBleedMm = bleed;
        if (int.TryParse(MaxSizeBox.Text, out var maxSize) && maxSize > 0) s.MpcFillMaximumSize = maxSize;

        s.MpcFillDefaultSortBy = GetComboTag(SortByBox) ?? "nameAscending";
        s.MpcFillDefaultMinDpi = int.TryParse(GetComboTag(DefaultMinDpiBox), out var minDpi) ? minDpi : 0;
        s.MpcFillDefaultMaxDpi = int.TryParse(GetComboTag(DefaultMaxDpiBox), out var maxDpi) ? maxDpi : 1500;
        s.MpcFillDefaultFuzzySearch = DefaultFuzzySearchBox.IsChecked == true;
        s.MpcFillFilterCardbacks = FilterCardbacksBox.IsChecked == true;

        var cardTypes = new List<string>();
        if (CardTypeCard.IsChecked == true) cardTypes.Add("CARD");
        if (CardTypeToken.IsChecked == true) cardTypes.Add("TOKEN");
        if (CardTypeCardback.IsChecked == true) cardTypes.Add("CARDBACK");
        if (cardTypes.Count == 0) cardTypes.Add("CARD");
        s.MpcFillCardTypes = cardTypes;

        var langs = new List<string>();
        if (LangEN.IsChecked == true) langs.Add("EN");
        if (LangJA.IsChecked == true) langs.Add("JA");
        if (LangFR.IsChecked == true) langs.Add("FR");
        if (LangDE.IsChecked == true) langs.Add("DE");
        if (LangES.IsChecked == true) langs.Add("ES");
        if (LangIT.IsChecked == true) langs.Add("IT");
        if (LangPT.IsChecked == true) langs.Add("PT");
        if (LangZH.IsChecked == true) langs.Add("ZH");
        if (LangRU.IsChecked == true) langs.Add("RU");
        if (LangAR.IsChecked == true) langs.Add("AR");
        if (LangSA.IsChecked == true) langs.Add("SA");
        s.MpcFillLanguages = langs;

        s.MpcFillExcludeNsfw = ExcludeNsfwBox.IsChecked == true;
        s.MpcFillExcludeAiArt = ExcludeAiArtBox.IsChecked == true;

        s.FrontArtLibraryPath = FrontLibPathBox.Text == "(default)" ? null : FrontLibPathBox.Text;
        s.BackArtLibraryPath = BackLibPathBox.Text == "(default)" ? null : BackLibPathBox.Text;

        _settingsService.Save();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    // ----------------------------------------------------------------

    private static void SelectComboByContent(ComboBox box, string value)
    {
        for (int i = 0; i < box.ItemCount; i++)
        {
            if (box.Items[i] is ComboBoxItem item && item.Content?.ToString() == value)
            {
                box.SelectedIndex = i;
                return;
            }
        }
        if (box.ItemCount > 0) box.SelectedIndex = 0;
    }

    private static void SelectComboByTag(ComboBox box, string tagValue)
    {
        for (int i = 0; i < box.ItemCount; i++)
        {
            if (box.Items[i] is ComboBoxItem item && item.Tag?.ToString() == tagValue)
            {
                box.SelectedIndex = i;
                return;
            }
        }
        if (box.ItemCount > 0) box.SelectedIndex = box.ItemCount - 1;
    }

    private static string? GetComboContent(ComboBox box)
        => (box.SelectedItem as ComboBoxItem)?.Content?.ToString();

    private static string? GetComboTag(ComboBox box)
        => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString();
}
