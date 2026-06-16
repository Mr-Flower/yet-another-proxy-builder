using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using SkiaSharp;

namespace MTGProxyBuilder.UI.Dialogs;

/// <summary>
/// "Blacken Border" editor: an edge flood-fill that turns a card's grey/grainy scan border into
/// pure black, with a threshold slider and a live original-vs-result preview. Returns the chosen
/// settings via <see cref="Result"/>.
///
/// Fork-specific dialog — self-contained, never touched by upstream.
/// </summary>
public partial class ImageAdjustmentWindow : Window
{
    private const int PreviewMaxSize = 360;

    private readonly ImageAdjustmentProcessor _processor = new();
    private SKBitmap? _previewSource;
    private bool _loading = true;
    private readonly ImageAdjustmentSettings _initial; // values when the dialog opened — used by Reset

    /// <summary>The settings the user applied, or null if they cancelled.</summary>
    public ImageAdjustmentSettings? Result { get; private set; }

    /// <summary>Which cards to apply to (this card / all by source). Valid when Result != null.</summary>
    public ImageAdjustmentTarget Target { get; private set; } = ImageAdjustmentTarget.ThisCard;

    public ImageAdjustmentWindow(string imagePath, ImageAdjustmentSettings current)
    {
        InitializeComponent();
        _initial = current;

        Loaded += (_, _) =>
        {
            LoadPreviewSource(imagePath);
            ApplySettingsToControls(current);
            _loading = false;
            Refresh();
        };
    }

    private void LoadPreviewSource(string imagePath)
    {
        try
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                ApplyBtn.IsEnabled = false;
                return;
            }

            using var full = SKBitmap.Decode(imagePath);
            if (full == null) { ApplyBtn.IsEnabled = false; return; }

            float scale = Math.Min(1f, (float)PreviewMaxSize / Math.Max(full.Width, full.Height));
            int w = Math.Max(1, (int)(full.Width * scale));
            int h = Math.Max(1, (int)(full.Height * scale));

            _previewSource = scale < 1f
                ? full.Resize(new SKImageInfo(w, h), SKFilterQuality.Medium)
                : full.Copy();

            if (_previewSource != null)
                OriginalPreview.Source = ToAvaloniaBitmap(_previewSource);
        }
        catch
        {
            ApplyBtn.IsEnabled = false;
        }
    }

    private void ApplySettingsToControls(ImageAdjustmentSettings s)
    {
        AutoApplyCheck.IsChecked = s.AutoApplyToScryfall;
        // Pre-check the box for a card that hasn't been adjusted yet (IsNoOp), so the user just clicks Apply.
        BlackenBorderCheck.IsChecked = s.BlackenBorder || s.IsNoOp;
        ThresholdSlider.Value = s.BorderThreshold > 0 ? s.BorderThreshold : 64;
        UpdateLabels();
    }

    private ImageAdjustmentSettings BuildSettings() => new()
    {
        Enabled = true,
        AutoApplyToScryfall = AutoApplyCheck.IsChecked == true,
        BlackenBorder = BlackenBorderCheck.IsChecked == true,
        BorderThreshold = (int)ThresholdSlider.Value
    };

    private void UpdateLabels()
    {
        ThresholdLabel.Text = ((int)ThresholdSlider.Value).ToString();
    }

    private void Refresh()
    {
        if (_loading) return;
        UpdateLabels();
        UpdateAdjustedPreview();
    }

    private void UpdateAdjustedPreview()
    {
        if (_previewSource == null) return;
        try
        {
            using var adjusted = _processor.Apply(_previewSource, BuildSettings());
            AdjustedPreview.Source = ToAvaloniaBitmap(adjusted);
        }
        catch { /* preview only — ignore */ }
    }

    private static Bitmap ToAvaloniaBitmap(SKBitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Encode(ms, SKEncodedImageFormat.Png, 100);
        ms.Seek(0, SeekOrigin.Begin);
        return new Bitmap(ms);
    }

    // ---- event handlers ----

    private void OnSliderChanged(object? sender, RangeBaseValueChangedEventArgs e) => Refresh();

    private void OnToggle(object? sender, RoutedEventArgs e) => Refresh();

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        // Return the sliders to where they were when the dialog opened (the card's original values).
        _loading = true;
        ApplySettingsToControls(_initial);
        _loading = false;
        Refresh();
        StatusLabel.Text = "";
    }

    private void OnSaveDefault(object? sender, RoutedEventArgs e)
    {
        try
        {
            new ImageAdjustmentStore().SaveDefault(BuildSettings());
            StatusLabel.Text = "Default saved.";
        }
        catch
        {
            StatusLabel.Text = "Could not save default.";
        }
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        // ComboBox order matches the ImageAdjustmentTarget enum (0..3).
        int idx = TargetCombo.SelectedIndex;
        Target = idx >= 0 && idx <= 3 ? (ImageAdjustmentTarget)idx : ImageAdjustmentTarget.ThisCard;
        Result = BuildSettings();
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close(false);
    }
}
