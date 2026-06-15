using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.Services;

namespace MTGProxyBuilder.UI.ViewModels;

// PDF / SVG export. Split out of MainViewModel as a partial class for navigability;
// behaviour is unchanged (partials compile into the same type).
public partial class MainViewModel
{
    private async Task ExportPdfAsync()
    {
        var path = await _dialogService.PickSaveFileAsync(
            "Export PDF", "PDF Files (*.pdf)|*.pdf", $"{ProjectName}.pdf");
        if (path == null) return;

        SyncCardsToProject();
        SetBusy("Downloading full-resolution art...");

        // Upgrade each card to its full-resolution art for the export only; restored afterwards so
        // the app keeps using the lightweight cached copies. Failures fall back to the cached image.
        var fullResSwaps = await UpgradeToFullResForExportAsync();
        SetBusy("Generating PDF...");
        try
        {
            if (await _pdfGeneratorService.GeneratePdfAsync(_currentProject, path))
                await OnPdfExportedAsync(path);
            else
                await ReportPdfExportFailedAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"PDF export failed: {ex.Message}";
            await _dialogService.ShowErrorAsync($"PDF generation error:\n{ex.Message}", "Export Failed");
        }
        finally
        {
            RestoreCachedArt(fullResSwaps); // keep the canvas/app on the lightweight cached copies
            ClearBusy();
        }
    }

    /// <summary>Reports a successful export, including any SVG cut-line sidecar files that were written.</summary>
    private async Task OnPdfExportedAsync(string pdfPath)
    {
        string svgInfo = await ExportSvgSidecarIfEnabledAsync(pdfPath);
        StatusText = $"PDF exported: {Path.GetFileName(pdfPath)}";
        await _dialogService.ShowInfoAsync($"PDF exported successfully!\n\n{pdfPath}{svgInfo}", "Export Complete");
    }

    private async Task ReportPdfExportFailedAsync()
    {
        StatusText = "PDF export failed";
        await _dialogService.ShowErrorAsync("Failed to generate PDF. Check that card images exist.", "Export Failed");
    }

    /// <summary>Writes SVG cut-line files next to the PDF when that option is on, and returns a short
    /// summary line for the success dialog (empty when disabled or nothing was written).</summary>
    private async Task<string> ExportSvgSidecarIfEnabledAsync(string pdfPath)
    {
        if (!_currentProject.PrintSettings.ExportSvgCutLines) return "";

        var svgService = new SvgCutLineService();
        string outputDir = Path.GetDirectoryName(pdfPath) ?? ".";
        string baseName = Path.GetFileNameWithoutExtension(pdfPath);
        var svgFiles = await svgService.GenerateSvgAsync(_currentProject, outputDir, baseName);
        return svgFiles.Count > 0
            ? $"\n\nSVG cut files ({svgFiles.Count}):\n" + string.Join("\n", svgFiles.Select(Path.GetFileName))
            : "";
    }

    /// <summary>Restores the cards' compressed cached art paths after a full-resolution export.</summary>
    private void RestoreCachedArt(List<(CardModel Card, string Front, string? Back)> swaps)
    {
        foreach (var (card, front, back) in swaps)
        {
            card.ArtworkPath = front;
            card.BackArtworkPath = back;
        }
        if (swaps.Count > 0) RefreshCanvas();
    }

    /// <summary>
    /// Downloads the full-resolution art for each card (from CardModel.FullResFrontUrl/BackUrl) and
    /// temporarily points the card at it for the export. Returns the list of (card, originalFront,
    /// originalBack) so the caller can restore the compressed paths afterwards. Cards without a
    /// full-res URL, or whose download fails, are left on their cached image (graceful fallback).
    /// The full-res cache key preserves the MPCFill "mpc_" marker so bleed handling stays correct.
    /// </summary>
    private async Task<List<(CardModel Card, string Front, string? Back)>> UpgradeToFullResForExportAsync()
    {
        var swaps = new List<(CardModel, string, string?)>();
        foreach (var card in Cards)
        {
            bool changed = false;
            string origFront = card.ArtworkPath;
            string? origBack = card.BackArtworkPath;

            if (!string.IsNullOrEmpty(card.FullResFrontUrl))
            {
                bool isMpc = BleedProcessor.ImageAlreadyHasBleed(origFront);
                // "fullpng_" (not the old "full_") so caches from the previous "large" Scryfall export
                // are not reused — the key is size-agnostic, so the prefix is what busts them.
                string key = (isMpc ? "mpc_full_" : "fullpng_") + (card.ScryfallId ?? card.CardId);
                var full = await _scryfallService.DownloadUrlToCacheAsync(card.FullResFrontUrl, key);
                if (!string.IsNullOrEmpty(full) && File.Exists(full)) { card.ArtworkPath = full; changed = true; }
            }
            if (!string.IsNullOrEmpty(card.FullResBackUrl) && !string.IsNullOrEmpty(origBack))
            {
                bool isMpc = BleedProcessor.ImageAlreadyHasBleed(origBack);
                string key = (isMpc ? "mpc_full_" : "fullpng_") + (card.ScryfallId ?? card.CardId) + "_back";
                var full = await _scryfallService.DownloadUrlToCacheAsync(card.FullResBackUrl, key);
                if (!string.IsNullOrEmpty(full) && File.Exists(full)) { card.BackArtworkPath = full; changed = true; }
            }

            if (changed) swaps.Add((card, origFront, origBack));
        }
        return swaps;
    }

    private async Task ExportSvgOnlyAsync()
    {
        var path = await _dialogService.PickSaveFileAsync(
            "Export SVG Cut Lines", "SVG Files|*.svg",
            $"{_currentProject.ProjectName}_cutlines");
        if (path == null) return;

        try
        {
            SetBusy("Generating SVG...");
            var svgService = new SvgCutLineService();
            string outputDir = Path.GetDirectoryName(path) ?? ".";
            string baseName = Path.GetFileNameWithoutExtension(path);
            var svgFiles = await svgService.GenerateSvgAsync(_currentProject, outputDir, baseName);

            if (svgFiles.Count > 0)
            {
                StatusText = $"SVG exported: {string.Join(", ", svgFiles.Select(Path.GetFileName))}";
                await _dialogService.ShowInfoAsync(
                    $"SVG cut lines exported!\n\n{string.Join("\n", svgFiles)}", "Export Complete");
            }
            else
            {
                StatusText = "No SVG files generated";
                await _dialogService.ShowWarningAsync(
                    "No cards to generate cut lines for.", "Export");
            }
        }
        catch (Exception ex)
        {
            StatusText = $"SVG export failed: {ex.Message}";
            await _dialogService.ShowErrorAsync($"SVG generation error:\n{ex.Message}", "Export Failed");
        }
        finally { ClearBusy(); }
    }
}
