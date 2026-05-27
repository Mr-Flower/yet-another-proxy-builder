using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.Services;
using SkiaSharp;

namespace MTGProxyBuilder.UI.ViewModels;

public class CardEditorViewModel : ViewModelBase
{
    private readonly CardCompositor _compositor = new();
    private readonly CustomCardSerializationService _serialization = new();
    private readonly IDialogService _dialogService;

    private CustomCardProject _project;
    private ObservableCollection<LayerBase> _layers;
    private LayerBase? _selectedLayer;
    private string? _filePath;
    private bool _hasUnsavedChanges;
    private string _statusText = "Ready";
    private int _refreshTrigger;

    public CardEditorViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;
        _project = new CustomCardProject();
        _layers = new ObservableCollection<LayerBase>();

        AddImageLayerCommand = new RelayCommand(_ => _ = AddImageLayerAsync());
        AddTextLayerCommand = new RelayCommand(_ => AddTextLayer());
        DeleteLayerCommand = new RelayCommand(_ => DeleteLayer(), _ => SelectedLayer != null);
        MoveLayerUpCommand = new RelayCommand(_ => MoveLayerUp(), _ => CanMoveUp());
        MoveLayerDownCommand = new RelayCommand(_ => MoveLayerDown(), _ => CanMoveDown());
        DuplicateLayerCommand = new RelayCommand(_ => DuplicateLayer(), _ => SelectedLayer != null);
        SaveCommand = new RelayCommand(_ => _ = SaveAsync());
        SaveAsCommand = new RelayCommand(_ => _ = SaveAsAsync());
        ExportImageCommand = new RelayCommand(_ => _ = ExportImageAsync());
        NewCommand = new RelayCommand(_ => NewProject());
        OpenCommand = new RelayCommand(_ => _ = OpenAsync());
    }

    // --- Properties ---

    public CustomCardProject Project
    {
        get => _project;
        private set => SetProperty(ref _project, value);
    }

    public ObservableCollection<LayerBase> Layers
    {
        get => _layers;
        private set => SetProperty(ref _layers, value);
    }

    public LayerBase? SelectedLayer
    {
        get => _selectedLayer;
        set
        {
            if (SetProperty(ref _selectedLayer, value))
            {
                ((RelayCommand)DeleteLayerCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DuplicateLayerCommand).RaiseCanExecuteChanged();
                ((RelayCommand)MoveLayerUpCommand).RaiseCanExecuteChanged();
                ((RelayCommand)MoveLayerDownCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string? FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        set => SetProperty(ref _hasUnsavedChanges, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public int RefreshTrigger
    {
        get => _refreshTrigger;
        set => SetProperty(ref _refreshTrigger, value);
    }

    public string WindowTitle
    {
        get
        {
            var name = Project.ProjectName;
            var changed = HasUnsavedChanges ? " *" : "";
            return $"{name}{changed} — Card Editor";
        }
    }

    // --- Commands ---

    public ICommand AddImageLayerCommand { get; }
    public ICommand AddTextLayerCommand { get; }
    public ICommand DeleteLayerCommand { get; }
    public ICommand MoveLayerUpCommand { get; }
    public ICommand MoveLayerDownCommand { get; }
    public ICommand DuplicateLayerCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand ExportImageCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand OpenCommand { get; }

    // --- Layer Operations ---

    public async Task AddImageLayerAsync()
    {
        var path = await _dialogService.PickOpenFileAsync(
            "Select Image", "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All Files|*.*");
        if (path == null) return;

        var layer = new ImageLayer
        {
            Name = Path.GetFileNameWithoutExtension(path),
            ImageSource = path,
            Width = 744,
            Height = 1039,
            ZOrder = GetNextZOrder()
        };

        try
        {
            using var bmp = SKBitmap.Decode(path);
            if (bmp != null) { layer.Width = bmp.Width; layer.Height = bmp.Height; }
        }
        catch { }

        AddLayer(layer);
    }

    public void AddTextLayer()
    {
        var layer = new TextLayer
        {
            Name = "Text Layer",
            Text = "Text",
            FontSize = 36,
            Width = 400,
            Height = 60,
            X = (Project.CardWidthPx - 400) / 2f,
            Y = Project.CardHeightPx / 2f,
            ZOrder = GetNextZOrder()
        };
        AddLayer(layer);
    }

    private void AddLayer(LayerBase layer)
    {
        Project.Layers.Add(layer);
        Layers.Add(layer);
        SelectedLayer = layer;
        MarkChanged();
    }

    public void DeleteLayer()
    {
        if (SelectedLayer == null) return;
        var layer = SelectedLayer;
        Project.Layers.Remove(layer);
        Layers.Remove(layer);
        SelectedLayer = Layers.LastOrDefault();
        MarkChanged();
    }

    public void MoveLayerUp()
    {
        if (SelectedLayer == null) return;
        var idx = Layers.IndexOf(SelectedLayer);
        if (idx >= Layers.Count - 1) return;

        var current = SelectedLayer;
        var above = Layers[idx + 1];
        (current.ZOrder, above.ZOrder) = (above.ZOrder, current.ZOrder);
        Layers.Move(idx, idx + 1);

        var projIdx = Project.Layers.IndexOf(current);
        var projAboveIdx = Project.Layers.IndexOf(above);
        if (projIdx >= 0 && projAboveIdx >= 0)
        {
            Project.Layers[projIdx] = above;
            Project.Layers[projAboveIdx] = current;
        }
        MarkChanged();
    }

    public void MoveLayerDown()
    {
        if (SelectedLayer == null) return;
        var idx = Layers.IndexOf(SelectedLayer);
        if (idx <= 0) return;

        var current = SelectedLayer;
        var below = Layers[idx - 1];
        (current.ZOrder, below.ZOrder) = (below.ZOrder, current.ZOrder);
        Layers.Move(idx, idx - 1);

        var projIdx = Project.Layers.IndexOf(current);
        var projBelowIdx = Project.Layers.IndexOf(below);
        if (projIdx >= 0 && projBelowIdx >= 0)
        {
            Project.Layers[projIdx] = below;
            Project.Layers[projBelowIdx] = current;
        }
        MarkChanged();
    }

    private void DuplicateLayer()
    {
        if (SelectedLayer == null) return;

        LayerBase copy;
        if (SelectedLayer is ImageLayer img)
        {
            copy = new ImageLayer
            {
                Name = img.Name + " (copy)", ImageSource = img.ImageSource,
                MaskEnabled = img.MaskEnabled, MaskPath = img.MaskPath,
                X = img.X + 20, Y = img.Y + 20, Width = img.Width, Height = img.Height,
                Rotation = img.Rotation, Opacity = img.Opacity, ZOrder = GetNextZOrder()
            };
        }
        else if (SelectedLayer is TextLayer txt)
        {
            copy = new TextLayer
            {
                Name = txt.Name + " (copy)", Text = txt.Text, FontFamily = txt.FontFamily,
                FontSize = txt.FontSize, FontColor = txt.FontColor, IsBold = txt.IsBold,
                IsItalic = txt.IsItalic, TextAlignment = txt.TextAlignment,
                LineSpacing = txt.LineSpacing, StrokeColor = txt.StrokeColor,
                StrokeWidth = txt.StrokeWidth,
                X = txt.X + 20, Y = txt.Y + 20, Width = txt.Width, Height = txt.Height,
                Rotation = txt.Rotation, Opacity = txt.Opacity, ZOrder = GetNextZOrder()
            };
        }
        else return;

        AddLayer(copy);
    }

    private bool CanMoveUp() => SelectedLayer != null && Layers.IndexOf(SelectedLayer) < Layers.Count - 1;
    private bool CanMoveDown() => SelectedLayer != null && Layers.IndexOf(SelectedLayer) > 0;
    private int GetNextZOrder() => Layers.Count > 0 ? Layers.Max(l => l.ZOrder) + 1 : 0;

    // --- File Operations ---

    public void NewProject()
    {
        Project = new CustomCardProject();
        Layers.Clear();
        SelectedLayer = null;
        FilePath = null;
        HasUnsavedChanges = false;
        StatusText = "New project";
        _compositor.ClearCaches();
        NotifyRefresh();
    }

    public async Task SaveAsync()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            await SaveAsAsync();
            return;
        }

        SyncLayersToProject();
        await _serialization.SaveProjectAsync(Project, FilePath);
        HasUnsavedChanges = false;
        StatusText = "Saved";
        OnPropertyChanged(nameof(WindowTitle));
    }

    public async Task SaveAsAsync()
    {
        var path = await _dialogService.PickSaveFileAsync(
            "Save Custom Card Project", "Custom Card Project|*.ccproj", Project.ProjectName);
        if (path == null) return;

        FilePath = path;
        SyncLayersToProject();
        await _serialization.SaveProjectAsync(Project, FilePath);
        HasUnsavedChanges = false;
        StatusText = $"Saved to {Path.GetFileName(FilePath)}";
        OnPropertyChanged(nameof(WindowTitle));
    }

    public async Task OpenAsync()
    {
        var path = await _dialogService.PickOpenFileAsync(
            "Open Custom Card Project", "Custom Card Project|*.ccproj");
        if (path == null) return;

        var project = await _serialization.LoadProjectAsync(path);
        if (project == null)
        {
            StatusText = "Failed to load project";
            return;
        }

        _compositor.ClearCaches();
        Project = project;
        Layers = new ObservableCollection<LayerBase>(project.Layers.OrderBy(l => l.ZOrder));
        SelectedLayer = Layers.FirstOrDefault();
        FilePath = path;
        HasUnsavedChanges = false;
        StatusText = $"Opened {Path.GetFileName(path)}";
        OnPropertyChanged(nameof(WindowTitle));
        NotifyRefresh();
    }

    public async Task ExportImageAsync()
    {
        var path = await _dialogService.PickSaveFileAsync(
            "Export Card Image", "PNG Image|*.png|JPEG Image|*.jpg", Project.ProjectName);
        if (path == null) return;

        SyncLayersToProject();
        using var bitmap = _compositor.RenderCard(Project);

        var format = Path.GetExtension(path).ToLowerInvariant() == ".jpg"
            ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;
        var quality = format == SKEncodedImageFormat.Jpeg ? 95 : 100;

        using var data = bitmap.Encode(format, quality);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);

        StatusText = $"Exported to {Path.GetFileName(path)}";
    }

    // --- Helpers ---

    private void SyncLayersToProject()
    {
        Project.Layers = Layers.ToList();
    }

    private void MarkChanged()
    {
        HasUnsavedChanges = true;
        OnPropertyChanged(nameof(WindowTitle));
        NotifyRefresh();
    }

    public void NotifyRefresh() => RefreshTrigger++;

    public CardCompositor Compositor => _compositor;
}
