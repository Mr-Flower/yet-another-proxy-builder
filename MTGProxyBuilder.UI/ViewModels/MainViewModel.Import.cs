using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.Services;

namespace MTGProxyBuilder.UI.ViewModels;

// Deck / text-list / MPCFill-XML import. Split out of MainViewModel (partial).
public partial class MainViewModel
{
    private async Task ImportMpcFillXmlAsync()
    {
        var path = await _dialogService.PickOpenFileAsync(
            "Import MPCFill Project (cards.xml)", "MPCFill XML (*.xml)|*.xml|All Files (*.*)|*.*");
        if (path == null) return;

        SetBusy("Parsing MPCFill XML...");
        try
        {
            var (project, parseError) = _importCoordinator.ParseXml(path);
            if (project == null || parseError != null)
            {
                ClearBusy();
                await _dialogService.ShowErrorAsync($"Failed to parse XML:\n{parseError}", "Import Error");
                return;
            }

            int totalSlots = project.Fronts.Sum(c => c.Slots.Count);
            BusyMessage = $"Found {project.Fronts.Count} unique card(s), {totalSlots} total slots";
            await Task.Delay(500);

            PushUndo();
            var result = await _importCoordinator.ImportXmlCardsAsync(project, onProgress: msg => BusyMessage = msg);

            BusyMessage = $"Adding {result.Cards.Count} cards to project...";
            await Task.Delay(50);

            Cards.CollectionChanged -= OnCardsCollectionChanged;
            foreach (var c in result.Cards) { ApplyDefaultBackArt(c); Cards.Add(c); }
            ArchiveToLibraryIfEnabled(result.Cards);
            Cards.CollectionChanged += OnCardsCollectionChanged;

            _currentProject.PageSettings.CenterGrid();
            ApplyFilterAndSort();

            int totalAdded = result.Cards.Sum(c => c.Quantity);
            string summary = $"Imported {result.Downloaded} card(s) ({totalAdded} total) from MPCFill XML";
            if (result.Failed > 0) summary += $"\n{result.Failed} image(s) failed to download";
            StatusText = summary;
            await _dialogService.ShowInfoAsync(summary, "Import Complete");
        }
        catch (Exception ex)
        {
            StatusText = $"Import failed: {ex.Message}";
            await _dialogService.ShowErrorAsync($"Import error:\n{ex.Message}", "Error");
        }
        finally { ClearBusy(); }
    }

    private async Task ImportDeckAsync()
    {
        var source = DeckImportService.DetectSource(ImportDeckUrl);
        if (source == DeckSource.Unknown)
        {
            await _dialogService.ShowWarningAsync(
                "Unrecognized URL. Paste a deck URL from:\n\n" +
                "- Moxfield (moxfield.com/decks/...)\n" +
                "- Archidekt (archidekt.com/decks/...)",
                "Invalid URL");
            return;
        }

        string sourceName = source.ToString();
        var ct = BeginCancellableBusy($"Connecting to {sourceName}...");

        try
        {
            BusyMessage = $"Fetching deck list from {sourceName}...";
            await Task.Delay(50);

            var (fetchedDeck, error) = await _importCoordinator.FetchDeckAsync(ImportDeckUrl);
            if (fetchedDeck is not { } deck || error != null)
            {
                ClearBusy();
                await _dialogService.ShowErrorAsync($"Failed to fetch deck:\n{error}", $"{sourceName} Error");
                return;
            }

            PushUndo();
            int uniqueCards = deck.Entries.Count;
            int totalQty = deck.Entries.Sum(e => e.Quantity);
            BusyMessage = $"Found deck: {deck.Name}\n{uniqueCards} unique cards, {totalQty} total ({deck.Format})";
            await Task.Delay(800);

            var result = await _importCoordinator.ImportDeckCardsAsync(
                deck, Cards, IgnoreDuplicates, UseMpcFill,
                MpcAdvMinDpi, MpcFuzzySearch, MpcUseFavoritesOnly,
                onProgress: msg => BusyMessage = msg, ct: ct);

            BusyMessage = $"Adding {result.Cards.Count} cards to project...";
            await Task.Delay(50);

            Cards.CollectionChanged -= OnCardsCollectionChanged;
            foreach (var c in result.Cards) { ApplyDefaultBackArt(c); Cards.Add(c); }
            ArchiveToLibraryIfEnabled(result.Cards);
            Cards.CollectionChanged += OnCardsCollectionChanged;

            _currentProject.PageSettings.CenterGrid();
            ApplyFilterAndSort();
            if (!ct.IsCancellationRequested) ImportDeckUrl = string.Empty;

            int totalAdded = result.Cards.Sum(c => c.Quantity);
            string summary = ct.IsCancellationRequested
                ? $"Import cancelled — kept {result.Cards.Count} card(s) fetched so far"
                : $"Imported {result.Cards.Count} unique card(s) ({totalAdded} total) from \"{deck.Name}\" ({sourceName})";
            if (result.SkippedDupes > 0) summary += $"\n{result.SkippedDupes} duplicate(s) skipped";
            if (result.Failed > 0) summary += $"\n{result.Failed} card(s) could not be found on Scryfall";
            StatusText = summary;
            await _dialogService.ShowInfoAsync(summary, ct.IsCancellationRequested ? "Import cancelled" : "Import Complete");
        }
        catch (Exception ex)
        {
            StatusText = $"Import failed: {ex.Message}";
            await _dialogService.ShowErrorAsync($"Import error:\n{ex.Message}", "Error");
        }
        finally { ClearBusy(); }
    }

    // fork-specific: import a pasted text decklist (resolves each name via Scryfall/MPCFill).
    private async Task ImportTextListAsync()
    {
        var deck = DeckImportService.ParseTextList(ImportDeckText);
        if (deck.Entries.Count == 0)
        {
            await _dialogService.ShowWarningAsync(
                "No card recognized. Paste a list with one card per line, e.g.:\n\n" +
                "2 Sol Ring\n1 Counterspell\nLightning Bolt\nt: Goblin",
                "Empty list");
            return;
        }

        var ct = BeginCancellableBusy("Importing pasted list...");
        try
        {
            PushUndo();

            // Opt-in: validate pasted names against the offline Scryfall bulk index before the import,
            // so typos are caught up front instead of after a long round of API lookups. Magic only.
            var unrecognized = new List<string>();
            if (_appSettings.Settings.UseScryfallBulkData && !IsYuGiOh)
            {
                BusyMessage = "Checking card database...";
                await _bulkData.EnsureLoadedAsync(_appSettings.Settings.BulkDataRefreshDays, m => BusyMessage = m);
                if (_bulkData.IsLoaded)
                    unrecognized = deck.Entries.Where(e => _bulkData.FindCard(e.CardName) == null)
                                               .Select(e => e.CardName).ToList();
            }

            var result = IsYuGiOh
                ? await _importCoordinator.ImportYuGiOhCardsAsync(
                    deck, Cards, IgnoreDuplicates,
                    onProgress: msg => BusyMessage = msg, ct: ct)
                : await _importCoordinator.ImportDeckCardsAsync(
                    deck, Cards, IgnoreDuplicates, UseMpcFill,
                    MpcAdvMinDpi, MpcFuzzySearch, MpcUseFavoritesOnly,
                    onProgress: msg => BusyMessage = msg, ct: ct);

            Cards.CollectionChanged -= OnCardsCollectionChanged;
            foreach (var c in result.Cards) { ApplyDefaultBackArt(c); Cards.Add(c); }
            ArchiveToLibraryIfEnabled(result.Cards);
            Cards.CollectionChanged += OnCardsCollectionChanged;

            _currentProject.PageSettings.CenterGrid();
            ApplyFilterAndSort();
            if (!ct.IsCancellationRequested) ImportDeckText = string.Empty;

            int totalAdded = result.Cards.Sum(c => c.Quantity);
            string summary = ct.IsCancellationRequested
                ? $"Import cancelled — kept {result.Cards.Count} card(s) fetched so far"
                : $"Imported {result.Cards.Count} card(s) ({totalAdded} total) from the pasted list";
            if (result.Failed > 0) summary += $"\n{result.Failed} card(s) not found on {(IsYuGiOh ? "YGOPRODeck" : "Scryfall")}";
            if (unrecognized.Count > 0)
            {
                summary += $"\n{unrecognized.Count} name(s) not in the card database (possible typos): "
                           + string.Join(", ", unrecognized.Take(10));
                Serilog.Log.Warning("Text import: {Count} unrecognized name(s): {Names}",
                    unrecognized.Count, string.Join(", ", unrecognized));
            }
            StatusText = summary;
            await _dialogService.ShowInfoAsync(summary, ct.IsCancellationRequested ? "Import cancelled" : "Import complete");
        }
        catch (Exception ex)
        {
            StatusText = $"Import failed: {ex.Message}";
            await _dialogService.ShowErrorAsync($"Import error:\n{ex.Message}", "Error");
        }
        finally { ClearBusy(); }
    }
}
