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
/// Live image-adjustment editor: brightness / contrast / saturation and a black-point
/// slider that crushes Scryfall's dark-grey scan borders to absolute black. Shows the
/// original next to a live preview and returns the chosen settings via <see cref="Result"/>.
///
/// Fork-specific dialog — self-contained, never touched by upstream.
/// </summary>
public partial class ImageAdjustmentWindow : Window
{
    private const int PreviewMaxSize = 360;

    private readonly ImageAdjustmentProcessor _processor = new();
    private SKBitmap? _previewSource;
    private bool _loading = true;

    /// <summary>The settings the user applied, or null if they cancelled.</summary>
    public ImageAdjustmentSettings? Result { get; private set; }

    /// <summary>Which cards to apply to (this card / all by source). Valid when Result != null.</summary>
    public ImageAdjustmentTarget Target { get; private set; } = ImageAdjustmentTarget.ThisCard;

    public ImageAdjustmentWindow(string imagePath, ImageAdjustmentSettings current)
    {
        InitializeComponent();

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
        EnabledCheck.IsChecked = s.Enabled;
        AutoApplyCheck.IsChecked = s.AutoApplyToScryfall;
        BrightnessSlider.Value = s.Brightness;
        ContrastSlider.Value = s.Contrast;
        SaturationSlider.Value = s.Saturation;
        BlackPointSlider.Value = s.BlackPoint;
        UpdateLabels();
    }

    private ImageAdjustmentSettings BuildSettings() => new()
    {
        Enabled = EnabledCheck.IsChecked == true,
        AutoApplyToScryfall = AutoApplyCheck.IsChecked == true,
        Brightness = (int)BrightnessSlider.Value,
        Contrast = (int)ContrastSlider.Value,
        Saturation = (int)SaturationSlider.Value,
        BlackPoint = (int)BlackPointSlider.Value
    };

    private void UpdateLabels()
    {
        BrightnessLabel.Text = ((int)BrightnessSlider.Value).ToString();
        ContrastLabel.Text = ((int)ContrastSlider.Value).ToString();
        SaturationLabel.Text = ((int)SaturationSlider.Value).ToString();
        BlackPointLabel.Text = ((int)BlackPointSlider.Value).ToString();
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
    private void OnCheckChanged(object? sender, RoutedEventArgs e) => Refresh();

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        _loading = true;
        EnabledCheck.IsChecked = true;
        BrightnessSlider.Value = 0;
        ContrastSlider.Value = 0;
        SaturationSlider.Value = 0;
        BlackPointSlider.Value = 0;
        _loading = false;
        Refresh();
        StatusLabel.Text = "";
    }

    private void OnSaveDefault(object? sender, RoutedEventArgs e)
    {
        try
        {
            new ImageAdjustmentStore().SaveDefault(BuildSettings());
            StatusLabel.Text = "Predefinito salvato.";
        }
        catch
        {
            StatusLabel.Text = "Impossibile salvare il predefinito.";
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
