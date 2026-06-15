using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.Services;

namespace MTGProxyBuilder.UI.ViewModels;

// Scryfall / MPCFill search and add-card. Split out of MainViewModel (partial).
public partial class MainViewModel
{
    private async Task ScryfallSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(ScryfallSearchQuery)) return;
        IsSearching = true;
        await SearchScryfallAsync();
        IsSearching = false;
        ClearBusy();
    }

    private async Task SearchScryfallAsync()
    {
        SetBusy("Searching Scryfall...");
        try
        {
            var (results, error) = await _searchCoordinator.SearchScryfallAsync(ScryfallSearchQuery);
            if (error != null)
            {
                ScryfallResults.Clear();
                StatusText = error;
                await _dialogService.ShowWarningAsync(error, "Search Error");
            }
            else
            {
                ScryfallResults = new ObservableCollection<ScryfallCard>(results);
                MpcFillResults.Clear();
                StatusText = $"Found {results.Count} result(s) on Scryfall";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Search failed: {ex.Message}";
            await _dialogService.ShowErrorAsync($"Unexpected error:\n{ex.Message}", "Error");
        }
    }

    private async Task SearchMpcFillAsync()
    {
        SetBusy("Searching MPCFill...");
        try
        {
            var (results, error) = await _searchCoordinator.SearchMpcFillAsync(
                ScryfallSearchQuery, MpcAdvMinDpi, MpcFuzzySearch, MpcUseFavoritesOnly, MpcAdvName);
            if (error != null)
            {
                MpcFillResults.Clear();
                StatusText = error;
                await _dialogService.ShowWarningAsync(error, "MPCFill Search Error");
            }
            else
            {
                MpcFillResults = new ObservableCollection<MpcFillCard>(results);
                ScryfallResults.Clear();
                string favInfo = MpcUseFavoritesOnly && MpcSourceManager.HasFavorites ? " (favorites only)" : "";
                StatusText = $"Found {MpcFillResults.Count} art version(s) on MPCFill{favInfo}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Search failed: {ex.Message}";
        }
    }

    private MpcFillSearchOptions BuildMpcFillSearchOptions()
        => _searchCoordinator.BuildSearchOptions(MpcAdvMinDpi, MpcFuzzySearch);

    private object[][]? GetMpcFillSources()
        => _searchCoordinator.GetSources(MpcUseFavoritesOnly);

    private async Task AddScryfallCardAsync()
    {
        if (SelectedScryfallCard == null) return;

        SetBusy($"Downloading artwork for {SelectedScryfallCard.Name}...");
        try
        {
            var frontPath = await _searchCoordinator.DownloadScryfallArtAsync(SelectedScryfallCard);
            string? backPath = null;
            if (SelectedScryfallCard.GetBackImageUrl() != null)
                backPath = await _searchCoordinator.DownloadScryfallArtAsync(SelectedScryfallCard, back: true);

            PushUndo();
            var card = SelectedScryfallCard.ToCardModel(frontPath ?? string.Empty, backPath);
            ApplyDefaultBackArt(card);
            _imageAdjust.AutoApply(card); // fork-specific: black-point etc. on Scryfall imports
            Cards.Add(card);
            ArchiveToLibraryIfEnabled(new[] { card });
            ApplyFilterAndSort();
            StatusText = $"Added: {card.Name} ({card.SetName})";
        }
        catch (Exception ex)
        {
            StatusText = $"Download failed: {ex.Message}";
        }
        finally { ClearBusy(); }
    }
}
