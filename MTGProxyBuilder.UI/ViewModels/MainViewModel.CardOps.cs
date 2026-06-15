using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.Services;

namespace MTGProxyBuilder.UI.ViewModels;

// Card operations: remove, browse/select front & back art, tokens, art-selector dialogs.
// Split out of MainViewModel (partial).
public partial class MainViewModel
{
    private void RemoveCard()
    {
        if (SelectedCard == null) return;
        PushUndo();
        Cards.Remove(SelectedCard);
        SelectedCard = null;
        StatusText = "Card removed";
    }

    private async Task BrowseFrontArtworkAsync()
    {
        if (SelectedCard == null) return;
        await ShowArtSelectorAsync(SelectedCard, ArtSelectorMode.Front);
    }

    private async Task BrowseBackArtworkAsync()
    {
        if (SelectedCard == null) return;
        await ShowArtSelectorAsync(SelectedCard, ArtSelectorMode.Back);
    }

    public async void OpenArtSelectorForCard(CardModel card, bool isShowingBack)
    {
        SelectedCard = card;
        await ShowArtSelectorAsync(card, isShowingBack ? ArtSelectorMode.Back : ArtSelectorMode.Front);
    }

    public async void SelectFrontArtForCards(List<int> cardIndices)
    {
        var targets = cardIndices
            .Where(i => i >= 0 && i < Cards.Count)
            .Select(i => Cards[i])
            .Distinct()
            .ToList();
        if (targets.Count == 0) return;

        var result = await _dialogService.ShowArtSelectorAsync(
            targets.First(), ArtSelectorMode.Front,
            _scryfallService, _mpcFillService, _imageCacheService,
            _backArtLibraryService, Cards.ToList(), GetMpcFillSources(),
            BuildMpcFillSearchOptions(), _frontArtLibraryService, _ygoService);

        if (result != null)
        {
            PushUndo();
            foreach (var c in targets) { c.ArtworkPath = result.ResultPath; c.FullResFrontUrl = null; }
            StatusText = $"Front art updated for {targets.Count} card(s)";
            RefreshCanvas();
        }
    }

    public async void SelectBackArtForCards(List<int> cardIndices)
    {
        var targets = cardIndices
            .Where(i => i >= 0 && i < Cards.Count)
            .Select(i => Cards[i])
            .Distinct()
            .ToList();
        if (targets.Count == 0) return;

        var result = await _dialogService.ShowArtSelectorAsync(
            targets.First(), ArtSelectorMode.Back,
            _scryfallService, _mpcFillService, _imageCacheService,
            _backArtLibraryService, Cards.ToList(), GetMpcFillSources(),
            BuildMpcFillSearchOptions(), _frontArtLibraryService, _ygoService);

        if (result != null)
        {
            PushUndo();
            foreach (var c in targets)
            {
                c.BackArtworkPath = result.ResultPath;
                c.FullResBackUrl = null;
                c.IncludeBack = true;
            }
            StatusText = $"Back art applied to {targets.Count} card(s)";
            RefreshBackArtLibrary();
            RefreshCanvas();
        }
    }

    public async void ApplyMajorityBackToCards(List<int> cardIndices)
    {
        var mostCommon = GetMostCommonBackArt();
        if (mostCommon == null)
        {
            await _dialogService.ShowInfoAsync(
                "No cards in the project have back art assigned.", "No Back Art");
            return;
        }

        PushUndo();
        int count = 0;
        foreach (var idx in cardIndices)
        {
            if (idx >= 0 && idx < Cards.Count)
            {
                Cards[idx].BackArtworkPath = mostCommon;
                Cards[idx].IncludeBack = true;
                count++;
            }
        }
        StatusText = $"Applied back art to {count} card(s)";
        RefreshCanvas();
    }

    public async void CreateTokensFromCards(List<CardModel> sourceCards)
    {
        // Real token generation: fetch the token cards associated with each source card
        // (via Scryfall all_parts) and add them as their own cards. Scryfall cards use
        // their id directly; cards without one (MPCFill, local images) are resolved by
        // name so tokens can still be created for them.
        var eligible = sourceCards
            .Where(c => !string.IsNullOrEmpty(c.ScryfallId) || !string.IsNullOrEmpty(c.Name))
            .ToList();
        if (eligible.Count == 0)
        {
            await _dialogService.ShowInfoAsync(
                "No valid card selected for token generation.",
                "No tokens");
            return;
        }

        SetBusy("Searching for related tokens...");
        try
        {
            string? commonBack = GetMostCommonBackArt();
            var newTokens = new List<CardModel>();
            var seenTokenIds = new HashSet<string>();

            foreach (var source in eligible)
            {
                var tokens = !string.IsNullOrEmpty(source.ScryfallId)
                    ? await _scryfallService.GetTokensForCardAsync(source.ScryfallId!)
                    : await _scryfallService.GetTokensForCardByNameAsync(source.Name);

                foreach (var (token, artworkPath) in tokens)
                {
                    // De-dup identical tokens shared by multiple selected cards.
                    if (!seenTokenIds.Add(token.Id)) continue;

                    var tokenCard = token.ToCardModel(artworkPath, null);
                    if (commonBack != null) { tokenCard.BackArtworkPath = commonBack; tokenCard.IncludeBack = true; }
                    else ApplyDefaultBackArt(tokenCard);

                    newTokens.Add(tokenCard);
                }
            }

            if (newTokens.Count == 0)
            {
                ClearBusy();
                await _dialogService.ShowInfoAsync(
                    "The selected cards have no associated tokens on Scryfall.",
                    "No tokens");
                return;
            }

            PushUndo();
            foreach (var t in newTokens)
                Cards.Add(t);

            ApplyFilterAndSort();
            StatusText = $"Created {newTokens.Count} token(s).";
        }
        catch (Exception ex)
        {
            StatusText = $"Token generation failed: {ex.Message}";
        }
        finally { ClearBusy(); }
    }

    public void CreateTokenFromCard(CardModel sourceCard) =>
        CreateTokensFromCards(new List<CardModel> { sourceCard });

    private string? GetMostCommonBackArt()
    {
        return Cards
            .Where(c => !string.IsNullOrEmpty(c.BackArtworkPath))
            .GroupBy(c => c.BackArtworkPath!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(c => c.Quantity))
            .FirstOrDefault()?.Key;
    }

    private async Task ShowArtSelectorAsync(CardModel card, ArtSelectorMode mode)
    {
        var result = await _dialogService.ShowArtSelectorAsync(
            card, mode, _scryfallService, _mpcFillService, _imageCacheService,
            _backArtLibraryService, Cards.ToList(), GetMpcFillSources(),
            BuildMpcFillSearchOptions(), _frontArtLibraryService, _ygoService);

        if (result == null) return;

        PushUndo();
        // The selector result is already full-resolution, so clear any stale full-res URL — otherwise
        // the export pre-pass would re-download the original printing over the user's choice.
        if (mode == ArtSelectorMode.Front)
        {
            if (result.ApplyToThisCopyOnly && card.Quantity > 1)
            {
                var copy = SplitCardCopy(card);
                if (copy != null) { copy.ArtworkPath = result.ResultPath; copy.FullResFrontUrl = null; SelectedCard = copy; }
                StatusText = $"Front art updated for one copy of {card.Name}";
            }
            else if (result.ApplyToSameName)
            {
                int count = 0;
                foreach (var c in Cards.Where(c => c.Name == card.Name))
                {
                    c.ArtworkPath = result.ResultPath;
                    c.FullResFrontUrl = null;
                    count++;
                }
                StatusText = $"Front art updated for {count} \"{card.Name}\" card(s)";
            }
            else
            {
                card.ArtworkPath = result.ResultPath;
                card.FullResFrontUrl = null;
                StatusText = $"Front art updated for {card.Name}";
            }
        }
        else
        {
            if (result.ApplyToThisCopyOnly && card.Quantity > 1)
            {
                var copy = SplitCardCopy(card);
                if (copy != null) { copy.BackArtworkPath = result.ResultPath; copy.FullResBackUrl = null; copy.IncludeBack = true; SelectedCard = copy; }
                StatusText = $"Back art updated for one copy of {card.Name}";
            }
            else if (result.ApplyToNoBack)
            {
                int count = 0;
                foreach (var c in Cards.Where(c => string.IsNullOrEmpty(c.BackArtworkPath)))
                {
                    c.BackArtworkPath = result.ResultPath;
                    c.FullResBackUrl = null;
                    c.IncludeBack = true;
                    count++;
                }
                StatusText = $"Back art applied to {count} card(s) without back art";
            }
            else
            {
                card.BackArtworkPath = result.ResultPath;
                card.FullResBackUrl = null;
                card.IncludeBack = true;
                StatusText = $"Back art updated for {card.Name}";
            }
        }
        RefreshBackArtLibrary();
        RefreshCanvas();
    }

    private async Task SelectBackArtForAllAsync()
    {
        if (Cards.Count == 0) return;
        await ShowBackArtSelectorAsync(Cards.ToList());
    }

    private async Task ShowBackArtSelectorAsync(List<CardModel> targetCards)
    {
        var result = await _dialogService.ShowArtSelectorAsync(
            targetCards.First(), ArtSelectorMode.Back,
            _scryfallService, _mpcFillService, _imageCacheService,
            _backArtLibraryService, Cards.ToList(), GetMpcFillSources(),
            BuildMpcFillSearchOptions(), _frontArtLibraryService, _ygoService);

        if (result == null) return;

        PushUndo();
        var targets = result.ApplyToNoBack
            ? Cards.Where(c => string.IsNullOrEmpty(c.BackArtworkPath)).ToList()
            : targetCards;

        foreach (var c in targets)
        {
            c.BackArtworkPath = result.ResultPath;
            c.IncludeBack = true;
        }
        StatusText = $"Back art applied to {targets.Count} card(s)";
        RefreshBackArtLibrary();
        RefreshCanvas();
    }
}
