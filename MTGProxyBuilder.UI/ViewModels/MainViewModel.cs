using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.Services;

namespace MTGProxyBuilder.UI.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
}

public class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public class MainViewModel : ViewModelBase
{
    private ProjectModel _currentProject;
    private ObservableCollection<CardModel> _cards;
    private CardModel? _selectedCard;
    private string _statusText = "Ready";
    private string? _currentFilePath;

    // Scryfall search
    private string _scryfallSearchQuery = string.Empty;
    private ObservableCollection<ScryfallCard> _scryfallResults = new();
    private ScryfallCard? _selectedScryfallCard;
    private bool _isSearching;
    private bool _isBusy;
    private string _busyMessage = string.Empty;
    private bool _busyIsCancellable;
    private CancellationTokenSource? _busyCts; // active when a cancellable operation (e.g. a fetch) runs
    private bool _hasUnsavedChanges;

    // Back art library
    private ObservableCollection<BackArtEntry> _backArtLibrary = new();
    private BackArtEntry? _selectedBackArt;

    // Sort and filter
    private string _filterText = string.Empty;
    private string _filterRarity = "All";
    private string _filterColor = "All";
    private string _sortBy = "Date Added";
    private bool _sortDescending;
    private ObservableCollection<CardModel> _filteredCards = new();

    // Page presets
    private string _selectedPagePreset = "A4";
    private CardSizePreset? _selectedCardSize;

    private readonly IDialogService _dialogService;
    private readonly ProjectSerializationService _serializationService;
    private readonly PdfGeneratorService _pdfGeneratorService;
    private readonly ScryfallService _scryfallService;
    private readonly YgoProDeckService _ygoService; // fork-specific: Yu-Gi-Oh! source
    private readonly ImageCacheService _imageCacheService;
    private BackArtLibraryService _backArtLibraryService;
    private FrontArtLibraryService _frontArtLibraryService;
    private readonly MoxfieldService _moxfieldService;
    private readonly ArchidektService _archidektService;
    private readonly DeckImportService _deckImportService;
    private readonly MpcFillService _mpcFillService;
    private readonly MpcFillXmlImportService _mpcXmlImportService;
    private readonly SearchCoordinator _searchCoordinator;
    private readonly ImportCoordinator _importCoordinator;
    private readonly UndoService _undoService = new();
    private readonly CacheManager _cacheManager = new();
    private readonly UpdateCheckService _updateService = new();
    private readonly AppSettingsService _appSettings = new();
    private readonly ImageAdjustmentService _imageAdjust = new(); // fork-specific
    private bool _updateAvailable;
    private string _updateMessage = string.Empty;
    private string _updateDownloadUrl = string.Empty;

    // Art source toggle
    private bool _useMpcFill;
    private ObservableCollection<MpcFillCard> _mpcFillResults = new();
    private MpcFillCard? _selectedMpcFillCard;
    private string? _searchPreviewUrl;

    // MPCFill advanced search
    private string _mpcAdvName = string.Empty;
    private int _mpcAdvMinDpi;
    private bool _mpcFuzzySearch = true;
    private bool _mpcUseFavoritesOnly;

    // Advanced search
    private bool _showAdvancedSearch;
    private string _advName = string.Empty;
    private string _advType = string.Empty;
    private string _advOracle = string.Empty;
    private string _advColors = string.Empty;
    private string _advIdentity = string.Empty;
    private string _advCmcOp = "=";
    private string _advCmcValue = string.Empty;
    private string _advRarity = string.Empty;
    private string _advSet = string.Empty;
    private string _advFormat = string.Empty;
    private string _advPowOp = "=";
    private string _advPowValue = string.Empty;
    private string _advTouOp = "=";
    private string _advTouValue = string.Empty;
    private string _advArtist = string.Empty;
    private string _advKeyword = string.Empty;
    private string _advIs = string.Empty;

    // Moxfield import
    private string _importDeckUrl = string.Empty;
    private bool _ignoreDuplicates = true;

    // Layout busy debounce timer
    private DispatcherTimer? _layoutBusyTimer;

    // Commands stored as RelayCommand for RaiseCanExecuteChanged
    private readonly RelayCommand _removeCardCmd;
    private readonly RelayCommand _browseFrontArtworkCmd;
    private readonly RelayCommand _browseBackArtworkCmd;
    private readonly RelayCommand _selectBackArtForAllCmd;
    private readonly RelayCommand _applyBackArtToSelectedCmd;
    private readonly RelayCommand _applyBackArtToAllCmd;
    private readonly RelayCommand _clearBackArtFromAllCmd;
    private readonly RelayCommand _removeBackArtFromLibraryCmd;
    private readonly RelayCommand _scryfallSearchCmd;
    private readonly RelayCommand _addScryfallCardCmd;
    private readonly RelayCommand _adjustImageCmd; // fork-specific
    private readonly RelayCommand _unlinkCardCopyCmd; // fork-specific
    private readonly RelayCommand _addMpcFillCardCmd;
    private readonly RelayCommand _clearAllCardsCmd;
    private readonly RelayCommand _undoCmd;
    private readonly RelayCommand _redoCmd;

    public MainViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;
        _imageCacheService = new ImageCacheService();
        _serializationService = new ProjectSerializationService();
        _pdfGeneratorService = new PdfGeneratorService();
        _scryfallService = new ScryfallService(_imageCacheService);
        _ygoService = new YgoProDeckService(_imageCacheService); // fork-specific: Yu-Gi-Oh! source
        _backArtLibraryService = new BackArtLibraryService(_appSettings.Settings.BackArtLibraryPath);
        _frontArtLibraryService = new FrontArtLibraryService(_appSettings.Settings.FrontArtLibraryPath);
        _moxfieldService = new MoxfieldService();
        _archidektService = new ArchidektService();
        _deckImportService = new DeckImportService(_moxfieldService, _archidektService);
        MpcSourceManager = new MpcFillSourceManager();
        _mpcFillService = new MpcFillService(_imageCacheService, MpcSourceManager);
        _mpcXmlImportService = new MpcFillXmlImportService(_mpcFillService, _imageCacheService);
        _searchCoordinator = new SearchCoordinator(_scryfallService, _mpcFillService, _appSettings, MpcSourceManager);
        _importCoordinator = new ImportCoordinator(_searchCoordinator, _deckImportService, _mpcXmlImportService, _ygoService);
        _mpcUseFavoritesOnly = _appSettings.Settings.MpcFillUseFavoritesOnly;
        _mpcAdvMinDpi = _appSettings.Settings.MpcFillDefaultMinDpi;
        _mpcFuzzySearch = _appSettings.Settings.MpcFillDefaultFuzzySearch;

        _ = _mpcFillService.EnsureSourcesLoadedAsync();

        _currentProject = new ProjectModel();
        _currentProject.PageSettings.PropertyChanged += OnPageSettingsChanged;
        _cards = new ObservableCollection<CardModel>();
        _cards.CollectionChanged += OnCardsCollectionChanged;

        NewProjectCommand = new RelayCommand(_ => NewProject());
        OpenProjectCommand = new RelayCommand(_ => _ = OpenProjectAsync());
        SaveProjectCommand = new RelayCommand(_ => _ = SaveProjectAsync());
        SaveProjectAsCommand = new RelayCommand(_ => _ = SaveProjectAsAsync());
        _undoCmd = new RelayCommand(_ => Undo(), _ => _undoService.CanUndo);
        UndoCommand = _undoCmd;
        _redoCmd = new RelayCommand(_ => Redo(), _ => _undoService.CanRedo);
        RedoCommand = _redoCmd;
        // RelayCommand has no auto-requery, so the Undo/Redo buttons stayed disabled after PushUndo.
        // Re-evaluate them whenever the undo/redo stacks change (SaveState/Undo/Redo/Clear).
        _undoService.StateChanged += () =>
        {
            _undoCmd.RaiseCanExecuteChanged();
            _redoCmd.RaiseCanExecuteChanged();
        };
        ExitCommand = new RelayCommand(_ => _dialogService.Shutdown());

        _removeCardCmd = new RelayCommand(_ => RemoveCard(), _ => SelectedCard != null);
        RemoveCardCommand = _removeCardCmd;
        _browseFrontArtworkCmd = new RelayCommand(_ => _ = BrowseFrontArtworkAsync(), _ => SelectedCard != null);
        BrowseFrontArtworkCommand = _browseFrontArtworkCmd;
        _browseBackArtworkCmd = new RelayCommand(_ => _ = BrowseBackArtworkAsync(), _ => SelectedCard != null);
        BrowseBackArtworkCommand = _browseBackArtworkCmd;
        _selectBackArtForAllCmd = new RelayCommand(_ => _ = SelectBackArtForAllAsync(), _ => Cards.Count > 0);
        SelectBackArtForAllCommand = _selectBackArtForAllCmd;

        _scryfallSearchCmd = new RelayCommand(_ => _ = ScryfallSearchAsync(), _ => !string.IsNullOrWhiteSpace(ScryfallSearchQuery));
        ScryfallSearchCommand = _scryfallSearchCmd;
        _addScryfallCardCmd = new RelayCommand(_ => _ = AddScryfallCardAsync(), _ => SelectedScryfallCard != null);
        AddScryfallCardCommand = _addScryfallCardCmd;
        _adjustImageCmd = new RelayCommand(_ => _ = AdjustImageAsync(), _ => SelectedCard != null); // fork-specific
        AdjustImageCommand = _adjustImageCmd;
        _unlinkCardCopyCmd = new RelayCommand(_ => UnlinkCardCopy(), _ => SelectedCard != null && SelectedCard.Quantity > 1); // fork-specific
        UnlinkCardCopyCommand = _unlinkCardCopyCmd;

        AddCardFromFileCommand = new RelayCommand(_ => _ = AddCardFromFileAsync());
        ExportPdfCommand = new RelayCommand(_ => _ = ExportPdfAsync());
        ExportSvgCommand = new RelayCommand(_ => _ = ExportSvgOnlyAsync());

        _removeBackArtFromLibraryCmd = new RelayCommand(_ => _ = RemoveBackArtFromLibraryAsync(), _ => SelectedBackArt != null);
        RemoveBackArtFromLibraryCommand = _removeBackArtFromLibraryCmd;
        AddBackArtToLibraryCommand = new RelayCommand(_ => _ = AddBackArtToLibraryAsync());
        _applyBackArtToSelectedCmd = new RelayCommand(_ => ApplyBackArtToSelected(), _ => SelectedBackArt != null && SelectedCard != null);
        ApplyBackArtToSelectedCommand = _applyBackArtToSelectedCmd;
        _applyBackArtToAllCmd = new RelayCommand(_ => _ = ApplyBackArtToAllAsync(), _ => SelectedBackArt != null && Cards.Count > 0);
        ApplyBackArtToAllCommand = _applyBackArtToAllCmd;
        _clearBackArtFromAllCmd = new RelayCommand(_ => _ = ClearBackArtFromAllAsync(), _ => Cards.Count > 0);
        ClearBackArtFromAllCommand = _clearBackArtFromAllCmd;

        SetPagePresetCommand = new RelayCommand(p => SetPagePreset(p as string));
        ToggleLandscapeCommand = new RelayCommand(_ => ToggleLandscape());

        ApplySortToProjectCommand = new RelayCommand(_ => ApplySortToProject());
        ClearFilterCommand = new RelayCommand(_ => ClearFilter());

        ImportDeckCommand = new RelayCommand(_ => _ = ImportDeckAsync(), _ => !string.IsNullOrWhiteSpace(ImportDeckUrl));
        ImportTextListCommand = new RelayCommand(_ => _ = ImportTextListAsync(), _ => !string.IsNullOrWhiteSpace(ImportDeckText));
        BuildAdvancedQueryCommand = new RelayCommand(_ => ApplyAdvancedQuery());
        ClearAdvancedSearchCommand = new RelayCommand(_ => ClearAdvancedSearch());

        LoadMpcSourcesCommand = new RelayCommand(_ => _ = LoadMpcSourcesAsync());
        ToggleMpcFavoriteFromResultCommand = new RelayCommand(p => ToggleFavoriteFromResult(p));
        ManageMpcSourcesCommand = new RelayCommand(_ => _ = ManageMpcSourcesAsync());
        ImportMpcFillXmlCommand = new RelayCommand(_ => _ = ImportMpcFillXmlAsync());
        ClearCacheCommand = new RelayCommand(_ => _ = ClearCacheAsync());
        ManageBackArtLibraryCommand = new RelayCommand(_ => _ = ManageBackArtLibraryAsync());
        ManageFrontArtLibraryCommand = new RelayCommand(_ => _ = ManageFrontArtLibraryAsync());
        DownloadUpdateCommand = new RelayCommand(_ => DownloadUpdate());
        DismissUpdateCommand = new RelayCommand(_ => UpdateAvailable = false);
        OpenSettingsCommand = new RelayCommand(_ => _ = OpenSettingsAsync());
        CancelBusyCommand = new RelayCommand(_ => { BusyMessage = "Cancelling…"; _busyCts?.Cancel(); });

        _addMpcFillCardCmd = new RelayCommand(_ => _ = AddMpcFillCardAsync(), _ => SelectedMpcFillCard != null);
        AddMpcFillCardCommand = _addMpcFillCardCmd;
        _clearAllCardsCmd = new RelayCommand(_ => _ = ClearAllCardsAsync(), _ => Cards.Count > 0);
        ClearAllCardsCommand = _clearAllCardsCmd;

        PrintModeValues = new ObservableCollection<PrintMode>(Enum.GetValues<PrintMode>());
        PagePresets = new ObservableCollection<string> { "A4", "A3", "Letter", "Legal", "Tabloid" };
        _selectedPagePreset = "A4";
        _selectedCardSize = FindCardSizePreset(63f, 88f); // Magic / poker default

        RefreshBackArtLibrary();
        ApplyFilterAndSort();
        _cacheManager.CleanupOnStartup();
    }

    public void UseSharedLibraries(FrontArtLibraryService frontLibrary, BackArtLibraryService backLibrary)
    {
        _frontArtLibraryService = frontLibrary;
        _backArtLibraryService = backLibrary;
        RefreshBackArtLibrary();
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            string currentVersion = GetAppVersion();
            var update = await _updateService.CheckForUpdateAsync(currentVersion);
            if (update?.IsUpdateAvailable == true)
            {
                UpdateAvailable = true;
                UpdateMessage = $"Version {update.LatestVersion} is available (you have {update.CurrentVersion})";
                UpdateDownloadUrl = update.DownloadUrl;
            }
        }
        catch { }
    }

    private void DownloadUpdate()
    {
        if (!string.IsNullOrEmpty(UpdateDownloadUrl))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = UpdateDownloadUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    // --- Properties ---

    public ProjectModel CurrentProject
    {
        get => _currentProject;
        set { SetProperty(ref _currentProject, value); OnPropertyChanged(nameof(ProjectName)); }
    }

    public ObservableCollection<CardModel> Cards
    {
        get => _cards;
        set
        {
            if (_cards != null) _cards.CollectionChanged -= OnCardsCollectionChanged;
            SetProperty(ref _cards, value!);
            if (_cards != null) _cards.CollectionChanged += OnCardsCollectionChanged;
        }
    }

    private void OnCardsCollectionChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        _currentProject.PageSettings.CenterGrid();
        ApplyFilterAndSort();
        _selectBackArtForAllCmd.RaiseCanExecuteChanged();
        _applyBackArtToAllCmd.RaiseCanExecuteChanged();
        _clearBackArtFromAllCmd.RaiseCanExecuteChanged();
        _clearAllCardsCmd.RaiseCanExecuteChanged();
    }

    private void OnPageSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!IsBusy)
        {
            BusyMessage = "Updating layout...";
            IsBusy = true;
        }

        _layoutBusyTimer?.Stop();
        _layoutBusyTimer ??= new DispatcherTimer();
        _layoutBusyTimer.Interval = TimeSpan.FromMilliseconds(200);
        _layoutBusyTimer.Tick += (_, _) => { _layoutBusyTimer.Stop(); ClearBusy(); };
        _layoutBusyTimer.Start();
    }

    public CardModel? SelectedCard
    {
        get => _selectedCard;
        set
        {
            if (SetProperty(ref _selectedCard, value))
            {
                _removeCardCmd.RaiseCanExecuteChanged();
                _browseFrontArtworkCmd.RaiseCanExecuteChanged();
                _browseBackArtworkCmd.RaiseCanExecuteChanged();
                _applyBackArtToSelectedCmd.RaiseCanExecuteChanged();
                _adjustImageCmd.RaiseCanExecuteChanged(); // fork-specific
                _unlinkCardCopyCmd.RaiseCanExecuteChanged(); // fork-specific
            }
        }
    }

    public string ProjectName
    {
        get => _currentProject.ProjectName;
        set { _currentProject.ProjectName = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public ObservableCollection<PrintMode> PrintModeValues { get; }

    public PrintMode SelectedPrintMode
    {
        get => _currentProject.PrintSettings.PrintMode;
        set { _currentProject.PrintSettings.PrintMode = value; OnPropertyChanged(); }
    }

    public ObservableCollection<OutlineAlignment> OutlineAlignmentOptions { get; } = new(Enum.GetValues<OutlineAlignment>());
    public ObservableCollection<OutlineType> OutlineTypeOptions { get; } = new(Enum.GetValues<OutlineType>());
    public ObservableCollection<LineType> LineTypeOptions { get; } = new(Enum.GetValues<LineType>());

    public OutlineAlignment SelectedOutlineAlignment
    {
        get => _currentProject.PrintSettings.OutlineAlignment;
        set { _currentProject.PrintSettings.OutlineAlignment = value; OnPropertyChanged(); }
    }

    public OutlineType SelectedOutlineType
    {
        get => _currentProject.PrintSettings.OutlineType;
        set { _currentProject.PrintSettings.OutlineType = value; OnPropertyChanged(); }
    }

    public LineType SelectedLineType
    {
        get => _currentProject.PrintSettings.OutlineLineType;
        set { _currentProject.PrintSettings.OutlineLineType = value; OnPropertyChanged(); }
    }

    public string ScryfallSearchQuery
    {
        get => _scryfallSearchQuery;
        set
        {
            if (SetProperty(ref _scryfallSearchQuery, value))
                _scryfallSearchCmd.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<ScryfallCard> ScryfallResults
    {
        get => _scryfallResults;
        set => SetProperty(ref _scryfallResults, value);
    }

    public ScryfallCard? SelectedScryfallCard
    {
        get => _selectedScryfallCard;
        set
        {
            if (SetProperty(ref _selectedScryfallCard, value))
            {
                SearchPreviewUrl = value?.GetImageUrl("normal");
                _addScryfallCardCmd.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsSearching
    {
        get => _isSearching;
        set => SetProperty(ref _isSearching, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string BusyMessage
    {
        get => _busyMessage;
        set => SetProperty(ref _busyMessage, value);
    }

    /// <summary>True while the current busy operation can be cancelled (shows the Cancel button).</summary>
    public bool BusyIsCancellable
    {
        get => _busyIsCancellable;
        set => SetProperty(ref _busyIsCancellable, value);
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        set => SetProperty(ref _hasUnsavedChanges, value);
    }

    private void MarkDirty() => HasUnsavedChanges = true;

    public bool UpdateAvailable
    {
        get => _updateAvailable;
        set => SetProperty(ref _updateAvailable, value);
    }

    public string UpdateMessage
    {
        get => _updateMessage;
        set => SetProperty(ref _updateMessage, value);
    }

    public string UpdateDownloadUrl
    {
        get => _updateDownloadUrl;
        set => SetProperty(ref _updateDownloadUrl, value);
    }

    public ICommand DownloadUpdateCommand { get; }
    public ICommand DismissUpdateCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand CancelBusyCommand { get; } // aborts the current cancellable busy operation (fetch)
    public string AppVersion { get; } = GetAppVersion();

    public static string GetAppVersion()
    {
        var asm = System.Reflection.Assembly.GetEntryAssembly();
        if (asm == null) return "dev";
        var attrs = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
        if (attrs.Length > 0 && attrs[0] is System.Reflection.AssemblyInformationalVersionAttribute attr)
            return attr.InformationalVersion?.Split('+')[0] ?? "dev";
        return asm.GetName().Version?.ToString(3) ?? "dev";
    }

    private int _refreshTrigger;
    public int RefreshTrigger
    {
        get => _refreshTrigger;
        set => SetProperty(ref _refreshTrigger, value);
    }

    private void RefreshCanvas() => RefreshTrigger++;

    public ObservableCollection<BackArtEntry> BackArtLibrary
    {
        get => _backArtLibrary;
        set => SetProperty(ref _backArtLibrary, value);
    }

    public BackArtEntry? SelectedBackArt
    {
        get => _selectedBackArt;
        set
        {
            if (SetProperty(ref _selectedBackArt, value))
            {
                _removeBackArtFromLibraryCmd.RaiseCanExecuteChanged();
                _applyBackArtToSelectedCmd.RaiseCanExecuteChanged();
                _applyBackArtToAllCmd.RaiseCanExecuteChanged();
            }
        }
    }

    public ObservableCollection<CardModel> FilteredCards
    {
        get => _filteredCards;
        set => SetProperty(ref _filteredCards, value);
    }

    public string FilterText
    {
        get => _filterText;
        set { if (SetProperty(ref _filterText, value)) ApplyFilterAndSort(); }
    }

    public string FilterRarity
    {
        get => _filterRarity;
        set { if (SetProperty(ref _filterRarity, value)) ApplyFilterAndSort(); }
    }

    public string FilterColor
    {
        get => _filterColor;
        set { if (SetProperty(ref _filterColor, value)) ApplyFilterAndSort(); }
    }

    public string SortBy
    {
        get => _sortBy;
        set { if (SetProperty(ref _sortBy, value)) ApplyFilterAndSort(); }
    }

    public bool SortDescending
    {
        get => _sortDescending;
        set { if (SetProperty(ref _sortDescending, value)) ApplyFilterAndSort(); }
    }

    public ObservableCollection<string> SortOptions { get; } = new()
    {
        "Date Added", "Name", "CMC", "Rarity", "Color", "Type", "Set", "Artist", "Collector #"
    };

    public ObservableCollection<string> RarityOptions { get; } = new()
    {
        "All", "common", "uncommon", "rare", "mythic"
    };

    public ObservableCollection<string> ColorOptions { get; } = new()
    {
        "All", "White", "Blue", "Black", "Red", "Green", "Colorless", "Multicolor"
    };

    public ICommand ApplySortToProjectCommand { get; }
    public ICommand ClearFilterCommand { get; }
    public ICommand ImportDeckCommand { get; }
    public ICommand ImportTextListCommand { get; } // fork-specific: paste a decklist
    public ICommand AddMpcFillCardCommand { get; }
    public ICommand ClearAllCardsCommand { get; }

    public string ImportDeckUrl
    {
        get => _importDeckUrl;
        set
        {
            if (SetProperty(ref _importDeckUrl, value))
                (ImportDeckCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private string _importDeckText = string.Empty;
    public string ImportDeckText
    {
        get => _importDeckText;
        set
        {
            if (SetProperty(ref _importDeckText, value))
                (ImportTextListCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    // fork-specific: which trading card game the "add by name" list resolves against.
    public List<string> GameOptions { get; } = new() { "Magic", "Yu-Gi-Oh!" };

    private string _selectedGame = "Magic";
    public string SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetProperty(ref _selectedGame, value))
            {
                OnPropertyChanged(nameof(IsYuGiOh));
                ApplyDefaultCardSizeForGame(); // Yu-Gi-Oh! -> 59x86, Magic -> 63x88
            }
        }
    }

    /// <summary>True when the pasted-list resolver should query YGOPRODeck instead of Scryfall.</summary>
    public bool IsYuGiOh => _selectedGame == "Yu-Gi-Oh!";

    /// <summary>Switches the sheet card size to match the selected game, so picking Yu-Gi-Oh! lays out
    /// 59 x 86 mm cards and Magic lays out 63 x 88 mm — no manual size change needed.</summary>
    private void ApplyDefaultCardSizeForGame()
    {
        var preset = IsYuGiOh ? FindCardSizePreset(59f, 86f) : FindCardSizePreset(63f, 88f);
        if (preset != null) SelectedCardSize = preset; // updates the dropdown, page settings and canvas
    }

    /// <summary>Finds the built-in card-size preset with the given dimensions, or null.</summary>
    private CardSizePreset? FindCardSizePreset(float widthMm, float heightMm)
        => CardSizePresets.FirstOrDefault(p => p.WidthMm == widthMm && p.HeightMm == heightMm);

    public bool IgnoreDuplicates
    {
        get => _ignoreDuplicates;
        set => SetProperty(ref _ignoreDuplicates, value);
    }

    public bool UseMpcFill
    {
        get => _useMpcFill;
        set => SetProperty(ref _useMpcFill, value);
    }

    public ObservableCollection<MpcFillCard> MpcFillResults
    {
        get => _mpcFillResults;
        set => SetProperty(ref _mpcFillResults, value);
    }

    public MpcFillCard? SelectedMpcFillCard
    {
        get => _selectedMpcFillCard;
        set
        {
            if (SetProperty(ref _selectedMpcFillCard, value))
            {
                SearchPreviewUrl = value?.MediumThumbnailUrl;
                _addMpcFillCardCmd.RaiseCanExecuteChanged();
            }
        }
    }

    public string? SearchPreviewUrl
    {
        get => _searchPreviewUrl;
        set => SetProperty(ref _searchPreviewUrl, value);
    }

    public string MpcAdvName { get => _mpcAdvName; set => SetProperty(ref _mpcAdvName, value); }
    public int MpcAdvMinDpi { get => _mpcAdvMinDpi; set => SetProperty(ref _mpcAdvMinDpi, value); }
    public bool MpcFuzzySearch { get => _mpcFuzzySearch; set => SetProperty(ref _mpcFuzzySearch, value); }
    public bool MpcUseFavoritesOnly
    {
        get => _mpcUseFavoritesOnly;
        set
        {
            if (SetProperty(ref _mpcUseFavoritesOnly, value))
            {
                _appSettings.Settings.MpcFillUseFavoritesOnly = value;
                _appSettings.Save();
            }
        }
    }
    public ObservableCollection<int> MpcDpiOptions { get; } = new() { 0, 300, 600, 800, 1200 };
    public MpcFillSourceManager MpcSourceManager { get; }
    public ObservableCollection<MpcFillSource> MpcSourceList { get; } = new();

    public ICommand LoadMpcSourcesCommand { get; }
    public ICommand ToggleMpcFavoriteFromResultCommand { get; }
    public ICommand ManageMpcSourcesCommand { get; }
    public ICommand ImportMpcFillXmlCommand { get; }
    public ICommand ClearCacheCommand { get; }
    public ICommand ManageBackArtLibraryCommand { get; }
    public ICommand ManageFrontArtLibraryCommand { get; }

    public string CacheSizeText
    {
        get
        {
            var size = _cacheManager.GetTotalCacheSizeBytes();
            return $"Cache: {CacheManager.FormatBytes(size)}";
        }
    }

    // Advanced Search
    public bool ShowAdvancedSearch { get => _showAdvancedSearch; set => SetProperty(ref _showAdvancedSearch, value); }
    public string AdvName { get => _advName; set => SetProperty(ref _advName, value); }
    public string AdvType { get => _advType; set => SetProperty(ref _advType, value); }
    public string AdvOracle { get => _advOracle; set => SetProperty(ref _advOracle, value); }
    public string AdvColors { get => _advColors; set => SetProperty(ref _advColors, value); }
    public string AdvIdentity { get => _advIdentity; set => SetProperty(ref _advIdentity, value); }
    public string AdvCmcOp { get => _advCmcOp; set => SetProperty(ref _advCmcOp, value); }
    public string AdvCmcValue { get => _advCmcValue; set => SetProperty(ref _advCmcValue, value); }
    public string AdvRarity { get => _advRarity; set => SetProperty(ref _advRarity, value); }
    public string AdvSet { get => _advSet; set => SetProperty(ref _advSet, value); }
    public string AdvFormat { get => _advFormat; set => SetProperty(ref _advFormat, value); }
    public string AdvPowOp { get => _advPowOp; set => SetProperty(ref _advPowOp, value); }
    public string AdvPowValue { get => _advPowValue; set => SetProperty(ref _advPowValue, value); }
    public string AdvTouOp { get => _advTouOp; set => SetProperty(ref _advTouOp, value); }
    public string AdvTouValue { get => _advTouValue; set => SetProperty(ref _advTouValue, value); }
    public string AdvArtist { get => _advArtist; set => SetProperty(ref _advArtist, value); }
    public string AdvKeyword { get => _advKeyword; set => SetProperty(ref _advKeyword, value); }
    public string AdvIs { get => _advIs; set => SetProperty(ref _advIs, value); }

    public ObservableCollection<string> ComparisonOps { get; } = new() { "=", "!=", "<", ">", "<=", ">=" };
    public ObservableCollection<string> RarityAdvOptions { get; } = new() { "", "common", "uncommon", "rare", "mythic" };
    public ObservableCollection<string> FormatOptions { get; } = new()
    {
        "", "standard", "pioneer", "modern", "legacy", "vintage", "pauper",
        "commander", "brawl", "historic", "explorer", "timeless", "oathbreaker"
    };
    public ObservableCollection<string> IsOptions { get; } = new()
    {
        "", "reprint", "full", "foil", "etched", "promo", "booster",
        "commander", "companion", "reserved", "vanilla", "funny",
        "transform", "mdfc", "split", "flip", "dfc",
        "fetchland", "shockland", "dual", "checkland", "painland"
    };

    public ICommand BuildAdvancedQueryCommand { get; }
    public ICommand ClearAdvancedSearchCommand { get; }

    private string BuildAdvancedQuery() => AdvancedQueryBuilder.Build(
        _advName, _advType, _advOracle, _advColors, _advIdentity,
        _advCmcOp, _advCmcValue, _advRarity, _advSet, _advFormat,
        _advPowOp, _advPowValue, _advTouOp, _advTouValue,
        _advArtist, _advKeyword, _advIs);

    private void ApplyAdvancedQuery()
    {
        ScryfallSearchQuery = BuildAdvancedQuery();
        if (!string.IsNullOrWhiteSpace(ScryfallSearchQuery) && ScryfallSearchCommand.CanExecute(null))
            ScryfallSearchCommand.Execute(null);
    }

    private void ClearAdvancedSearch()
    {
        AdvName = AdvType = AdvOracle = AdvColors = AdvIdentity = string.Empty;
        AdvCmcOp = AdvPowOp = AdvTouOp = "=";
        AdvCmcValue = AdvRarity = AdvSet = AdvFormat = string.Empty;
        AdvPowValue = AdvTouValue = AdvArtist = AdvKeyword = AdvIs = string.Empty;
        ScryfallSearchQuery = string.Empty;
    }

    public ObservableCollection<CardSizePreset> CardSizePresets { get; } = new(CardSizePreset.BuiltInPresets);

    public CardSizePreset? SelectedCardSize
    {
        get => _selectedCardSize;
        set
        {
            if (SetProperty(ref _selectedCardSize, value) && value != null)
            {
                _currentProject.PageSettings.CardWidthMm = value.WidthMm;
                _currentProject.PageSettings.CardHeightMm = value.HeightMm;
                StatusText = $"Card size: {value.Name} ({value.WidthMm} x {value.HeightMm} mm)";
            }
        }
    }

    /// <summary>Selects the card-size dropdown entry matching the loaded project's dimensions (or none
    /// if it's a custom size), so the dropdown reflects the project after opening it.</summary>
    private void SyncCardSizeToPageSettings()
    {
        var ps = _currentProject.PageSettings;
        _selectedCardSize = FindCardSizePreset(ps.CardWidthMm, ps.CardHeightMm);
        OnPropertyChanged(nameof(SelectedCardSize));
    }

    public ObservableCollection<string> PagePresets { get; }

    public string SelectedPagePreset
    {
        get => _selectedPagePreset;
        set
        {
            if (SetProperty(ref _selectedPagePreset, value) && value != null)
                _currentProject.PageSettings.ApplyPagePreset(value);
        }
    }

    // --- Commands ---

    public ICommand NewProjectCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand SaveProjectCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public UndoService UndoServiceInstance => _undoService;
    public ICommand SaveProjectAsCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand AddCardFromFileCommand { get; }
    public ICommand RemoveCardCommand { get; }
    public ICommand BrowseFrontArtworkCommand { get; }
    public ICommand BrowseBackArtworkCommand { get; }
    public ICommand SelectBackArtForAllCommand { get; }
    public ICommand ScryfallSearchCommand { get; }
    public ICommand AddScryfallCardCommand { get; }
    public ICommand AdjustImageCommand { get; } // fork-specific
    public ICommand UnlinkCardCopyCommand { get; } // fork-specific
    public ICommand ExportPdfCommand { get; }
    public ICommand ExportSvgCommand { get; }
    public ICommand AddBackArtToLibraryCommand { get; }
    public ICommand RemoveBackArtFromLibraryCommand { get; }
    public ICommand ApplyBackArtToSelectedCommand { get; }
    public ICommand ApplyBackArtToAllCommand { get; }
    public ICommand ClearBackArtFromAllCommand { get; }
    public ICommand SetPagePresetCommand { get; }
    public ICommand ToggleLandscapeCommand { get; }

    // --- Undo / Redo ---

    private void PushUndo() { _undoService.SaveState(Cards); MarkDirty(); }

    private void Undo()
    {
        var restored = _undoService.Undo(Cards);
        if (restored != null) RestoreCards(restored);
    }

    private void Redo()
    {
        var restored = _undoService.Redo(Cards);
        if (restored != null) RestoreCards(restored);
    }

    private void RestoreCards(List<CardModel> cards)
    {
        Cards.CollectionChanged -= OnCardsCollectionChanged;
        Cards.Clear();
        foreach (var c in cards) Cards.Add(c);
        Cards.CollectionChanged += OnCardsCollectionChanged;
        _currentProject.PageSettings.CenterGrid();
        ApplyFilterAndSort();
        SelectedCard = null;
        StatusText = "Undo/Redo applied";
        _undoCmd.RaiseCanExecuteChanged();
        _redoCmd.RaiseCanExecuteChanged();
    }

    // --- Command Implementations ---

    private void NewProject()
    {
        PushUndo();
        _undoService.Clear();
        _currentProject = new ProjectModel();
        _currentProject.PageSettings.PropertyChanged += OnPageSettingsChanged;
        Cards.Clear();
        _currentFilePath = null;
        SelectedCard = null;
        OnPropertyChanged(nameof(CurrentProject));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(SelectedPrintMode));
        HasUnsavedChanges = false;
        StatusText = "New project created";
    }

    public void LoadFromProject(ProjectModel project, string filePath)
    {
        _currentProject = project;
        _currentProject.PageSettings.PropertyChanged += OnPageSettingsChanged;
        _currentFilePath = filePath;
        Cards = new ObservableCollection<CardModel>(project.Cards);
        SelectedCard = null;
        _selectedPagePreset = DetectPagePreset(project.PageSettings);
        SyncCardSizeToPageSettings();
        HasUnsavedChanges = false;
        OnPropertyChanged(nameof(CurrentProject));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(SelectedPagePreset));
        OnPropertyChanged(nameof(SelectedPrintMode));
        OnPropertyChanged(nameof(SelectedOutlineAlignment));
        OnPropertyChanged(nameof(SelectedOutlineType));
        OnPropertyChanged(nameof(SelectedLineType));
    }

    private async Task OpenProjectAsync()
    {
        var path = await _dialogService.PickOpenFileAsync(
            "Open Project", "MTG Project Files (*.mtgproj)|*.mtgproj|All Files (*.*)|*.*");
        if (path == null) return;

        SetBusy("Opening project...");
        try
        {
            var project = await _serializationService.LoadProjectAsync(path);
            if (project == null)
            {
                await _dialogService.ShowErrorAsync("Failed to load project file.", "Error");
                return;
            }

            _currentProject = project;
            _currentProject.PageSettings.PropertyChanged += OnPageSettingsChanged;
            _currentFilePath = path;
            Cards = new ObservableCollection<CardModel>(project.Cards);
            SelectedCard = null;
            _selectedPagePreset = DetectPagePreset(project.PageSettings);
        SyncCardSizeToPageSettings();
            OnPropertyChanged(nameof(CurrentProject));
            OnPropertyChanged(nameof(ProjectName));
            OnPropertyChanged(nameof(SelectedPagePreset));
            OnPropertyChanged(nameof(SelectedPrintMode));
            HasUnsavedChanges = false;
            StatusText = $"Opened: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync($"Error opening project:\n{ex.Message}", "Error");
        }
        finally { ClearBusy(); }
    }

    private async Task SaveProjectAsync()
    {
        if (_currentFilePath == null) { await SaveProjectAsAsync(); return; }

        SyncCardsToProject();
        SetBusy("Saving project...");
        try
        {
            bool success = await _serializationService.SaveProjectAsync(_currentProject, _currentFilePath);
            if (success) HasUnsavedChanges = false;
            StatusText = success ? "Project saved" : "Failed to save project";
        }
        finally { ClearBusy(); }
    }

    private async Task SaveProjectAsAsync()
    {
        var path = await _dialogService.PickSaveFileAsync(
            "Save Project As", "MTG Project Files (*.mtgproj)|*.mtgproj", $"{ProjectName}.mtgproj");
        if (path == null) return;

        _currentFilePath = path;
        SyncCardsToProject();
        SetBusy("Saving project...");
        try
        {
            bool success = await _serializationService.SaveProjectAsync(_currentProject, _currentFilePath);
            if (success) HasUnsavedChanges = false;
            StatusText = success ? $"Saved: {Path.GetFileName(path)}" : "Failed to save project";
        }
        finally { ClearBusy(); }
    }

    private async Task AddCardFromFileAsync()
    {
        var paths = await _dialogService.PickOpenFilesAsync(
            "Select Card Artwork", "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*");
        if (paths.Length == 0) return;

        // Ask how to treat these local images for bleed (and what source they count as).
        bool? asMpcChoice = await AskLocalImageBleedStyleAsync();
        if (asMpcChoice is not bool asMpc) return; // cancelled

        PushUndo();
        SetBusy($"Loading {paths.Length} image(s)...");
        await Task.Delay(50);

        Cards.CollectionChanged -= OnCardsCollectionChanged;
        foreach (var filePath in paths)
            Cards.Add(CreateCardFromLocalFile(filePath, asMpc));
        Cards.CollectionChanged += OnCardsCollectionChanged;
        _currentProject.PageSettings.CenterGrid();
        ApplyFilterAndSort();
        RefreshCanvas();
        StatusText = $"Added {paths.Length} card(s) ({(asMpc ? "MPCFill" : "Scryfall")} bleed)";
        ClearBusy();
    }

    /// <summary>Asks how local images should be treated for bleed. Returns true for MPCFill style
    /// (crop the built-in 1/8"), false for Scryfall style (add bleed), or null if cancelled.</summary>
    private async Task<bool?> AskLocalImageBleedStyleAsync()
    {
        var choice = await _dialogService.ConfirmCancelAsync(
            "How should these images be treated for bleed?\n\n" +
            "• Yes = MPCFill style: they already include the 1/8\" bleed border (it will be trimmed)\n" +
            "• No = Scryfall style: bare card, the bleed will be added for you",
            "Local image bleed");
        return choice == MessageResult.Cancel ? null : choice == MessageResult.Yes;
    }

    /// <summary>Builds a card from a local image file, copying it into the front art library (named, so
    /// it's reusable by name) with the right bleed marker, and falling back to the original path if the
    /// library copy fails.</summary>
    private CardModel CreateCardFromLocalFile(string filePath, bool asMpc)
    {
        string name = Path.GetFileNameWithoutExtension(filePath);
        var entry = _frontArtLibraryService.AddFromFile(
            filePath, name, asMpc ? "Local MPCFill" : "Local", markAsBled: asMpc);

        var card = new CardModel
        {
            Name = name,
            ArtworkPath = entry?.FilePath ?? filePath,
            Source = asMpc ? CardSource.MpcFill : CardSource.Scryfall
        };
        ApplyDefaultBackArt(card);
        return card;
    }

    /// <summary>
    /// fork-specific: intentionally a no-op. Downloaded art is kept ONLY in the image cache (browsed
    /// as thumbnails, full file fetched on export); it is no longer auto-copied into the art library.
    /// The library is user-managed — add art explicitly via "+ Add to Library" in the art selector.
    /// </summary>
    private void ArchiveToLibraryIfEnabled(IEnumerable<CardModel> cards) { }

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

    // fork-specific: split one copy off a stacked card (Quantity > 1) so it can be edited
    // independently. Decrements the original's quantity and inserts a distinct Quantity=1 clone
    // right after it, then selects the clone. Total card count is preserved.
    private void UnlinkCardCopy()
    {
        var card = SelectedCard;
        if (card == null || card.Quantity <= 1) return;

        PushUndo();
        var copy = SplitCardCopy(card);
        if (copy == null) return;

        SelectedCard = copy; // edits now affect only this single copy
        _unlinkCardCopyCmd.RaiseCanExecuteChanged();
        RefreshCanvas();
        StatusText = $"Unlinked 1 copy of \"{copy.Name}\" — now editable separately.";
    }

    /// <summary>
    /// Splits one copy off a stacked card (Quantity > 1): decrements the original and inserts a
    /// distinct Quantity=1 clone right after it. Returns the new clone (or null if not a stack).
    /// Caller is responsible for PushUndo()/refresh.
    /// </summary>
    private CardModel? SplitCardCopy(CardModel card)
    {
        if (card.Quantity <= 1) return null;
        int idx = Cards.IndexOf(card);
        card.Quantity -= 1;
        var copy = card.Clone();
        copy.Quantity = 1;
        if (idx >= 0) Cards.Insert(idx + 1, copy);
        else Cards.Add(copy);
        return copy;
    }

    // fork-specific: open the image adjustment editor for the selected card and bake the result.
    // fork-specific: a card counts as MPCFill if it's tagged so OR its art file carries the
    // "mpc_" full-bleed marker. Cards imported by name keep Source=Scryfall even when their
    // art was pulled from MPCFill, so the filename marker is the reliable signal for the
    // source-based darken/adjust filters (otherwise "Solo Scryfall" would catch MPCFill cards).
    private static bool IsMpcFillArt(CardModel c) =>
        c.Source == CardSource.MpcFill || BleedProcessor.ImageAlreadyHasBleed(c.ArtworkPath);

    private async Task AdjustImageAsync()
    {
        var card = SelectedCard;
        if (card == null) return;
        if (string.IsNullOrEmpty(card.ArtworkPath))
        {
            await _dialogService.ShowInfoAsync("The selected card has no front image.", "Adjust Image");
            return;
        }

        var store = _imageAdjust.OpenStore();
        string original = store.ResolveOriginal(card.ArtworkPath);
        var current = store.GetSettingsFor(original);

        var result = await _dialogService.ShowImageAdjustmentAsync(original, current);
        if (result == null) return;

        var settings = result.Settings;

        // Decide which cards to bake based on the chosen target (this card / by source / both).
        var targets = result.Target switch
        {
            ImageAdjustmentTarget.AllScryfall => Cards.Where(c => !IsMpcFillArt(c)),
            ImageAdjustmentTarget.AllMpcFill  => Cards.Where(IsMpcFillArt),
            ImageAdjustmentTarget.AllBoth     => Cards.Where(c => !string.IsNullOrEmpty(c.ArtworkPath)),
            _                                 => new[] { card }.AsEnumerable()
        };

        PushUndo();
        int n = 0;
        foreach (var c in targets)
        {
            if (string.IsNullOrEmpty(c.ArtworkPath)) continue;
            _imageAdjust.BakeFront(c, settings);
            n++;
        }
        RefreshCanvas();

        StatusText = result.Target == ImageAdjustmentTarget.ThisCard
            ? (settings.IsNoOp ? "Image restored to the original." : "Image adjustment applied.")
            : $"Adjustment applied to {n} card(s).";
    }

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
                string key = (isMpc ? "mpc_full_" : "full_") + (card.ScryfallId ?? card.CardId);
                var full = await _scryfallService.DownloadUrlToCacheAsync(card.FullResFrontUrl, key);
                if (!string.IsNullOrEmpty(full) && File.Exists(full)) { card.ArtworkPath = full; changed = true; }
            }
            if (!string.IsNullOrEmpty(card.FullResBackUrl) && !string.IsNullOrEmpty(origBack))
            {
                bool isMpc = BleedProcessor.ImageAlreadyHasBleed(origBack);
                string key = (isMpc ? "mpc_full_" : "full_") + (card.ScryfallId ?? card.CardId) + "_back";
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

    // --- Back Art Library ---

    private void RefreshBackArtLibrary()
        => BackArtLibrary = new ObservableCollection<BackArtEntry>(_backArtLibraryService.Entries);

    private async Task AddBackArtToLibraryAsync()
    {
        var path = await _dialogService.PickOpenFileAsync(
            "Add Back Art to Library",
            "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*");
        if (path == null) return;

        SetBusy("Adding to back art library...");
        await Task.Delay(50);

        var entry = _backArtLibraryService.AddFromFile(path);
        if (entry != null)
        {
            RefreshBackArtLibrary();
            SelectedBackArt = BackArtLibrary.FirstOrDefault(e => e.Id == entry.Id);
            StatusText = $"Added '{entry.Name}' to back art library";
        }
        ClearBusy();
    }

    private async Task RemoveBackArtFromLibraryAsync()
    {
        if (SelectedBackArt == null) return;

        bool confirmed = await _dialogService.ConfirmAsync(
            $"Remove '{SelectedBackArt.Name}' from the library?\n\nThis will not remove it from cards that already use it.",
            "Remove Back Art");

        if (!confirmed) return;

        _backArtLibraryService.Remove(SelectedBackArt.Id);
        RefreshBackArtLibrary();
        SelectedBackArt = null;
        StatusText = "Back art removed from library";
    }

    private void ApplyBackArtToSelected()
    {
        if (SelectedBackArt == null || SelectedCard == null) return;
        SelectedCard.BackArtworkPath = SelectedBackArt.FilePath;
        SelectedCard.IncludeBack = true;
        StatusText = $"Applied '{SelectedBackArt.Name}' to {SelectedCard.Name}";
    }

    private async Task ApplyBackArtToAllAsync()
    {
        if (SelectedBackArt == null || Cards.Count == 0) return;
        SetBusy($"Applying back art to {Cards.Count} card(s)...");
        await Task.Delay(50);
        foreach (var card in Cards) { card.BackArtworkPath = SelectedBackArt.FilePath; card.IncludeBack = true; }
        StatusText = $"Applied '{SelectedBackArt.Name}' to all {Cards.Count} card(s)";
        ClearBusy();
    }

    private async Task ClearBackArtFromAllAsync()
    {
        if (Cards.Count == 0) return;
        SetBusy("Clearing back art...");
        await Task.Delay(50);
        foreach (var card in Cards) { card.BackArtworkPath = null; card.IncludeBack = false; }
        StatusText = $"Cleared back art from all {Cards.Count} card(s)";
        ClearBusy();
    }

    // --- MPCFill ---

    private async Task ManageMpcSourcesAsync()
    {
        if (IsBusy) return;
        SetBusy("Loading MPCFill sources...");
        try
        {
            var error = await _mpcFillService.EnsureSourcesLoadedAsync();
            ClearBusy();

            if (error != null)
            {
                await _dialogService.ShowWarningAsync($"Could not load sources from MPCFill:\n\n{error}", "MPCFill Error");
                StatusText = error;
                return;
            }

            if (MpcSourceManager.AllSources.Count == 0)
            {
                await _dialogService.ShowWarningAsync("No sources were returned from MPCFill.", "MPCFill Error");
                return;
            }

            await _dialogService.ShowMpcSourceManagerAsync(MpcSourceManager, _mpcFillService);
            StatusText = $"MPCFill: {MpcSourceManager.FavoritePks.Count} favorite source(s)";
        }
        catch (Exception ex)
        {
            ClearBusy();
            await _dialogService.ShowErrorAsync($"Unexpected error:\n{ex.Message}", "Error");
        }
    }

    private async Task LoadMpcSourcesAsync()
    {
        if (IsBusy) return;
        SetBusy("Loading MPCFill sources...");
        var error = await _mpcFillService.EnsureSourcesLoadedAsync();
        MpcSourceList.Clear();
        foreach (var s in MpcSourceManager.AllSources) MpcSourceList.Add(s);
        ClearBusy();
        StatusText = error ?? $"Loaded {MpcSourceList.Count} MPCFill sources ({MpcSourceManager.FavoritePks.Count} favorites)";
    }

    private void ToggleFavoriteFromResult(object? param)
    {
        string? sourceName = null;
        int sourcePk = -1;

        if (param is MpcFillCard card)
        {
            sourceName = card.Source;
            sourcePk = card.SourceId;
        }
        else if (param is string name)
        {
            sourceName = name;
            var src = MpcSourceManager.GetByName(name);
            if (src != null) sourcePk = src.Pk;
        }

        if (sourcePk <= 0) return;

        MpcSourceManager.ToggleFavorite(sourcePk);
        bool isFav = MpcSourceManager.IsFavorite(sourcePk);
        StatusText = isFav
            ? $"Added '{sourceName}' to MPCFill favorites"
            : $"Removed '{sourceName}' from MPCFill favorites";

        foreach (var s in MpcSourceList) s.IsFavorite = MpcSourceManager.IsFavorite(s.Pk);
    }

    private async Task AddMpcFillCardAsync()
    {
        if (SelectedMpcFillCard == null) return;
        SetBusy($"Downloading art: {SelectedMpcFillCard.Name}...");
        try
        {
            var (card, _) = await _importCoordinator.AddMpcFillCardAsync(SelectedMpcFillCard);
            if (card != null)
            {
                PushUndo();
                ApplyDefaultBackArt(card);
                Cards.Add(card);
                ArchiveToLibraryIfEnabled(new[] { card });
                ApplyFilterAndSort();
                StatusText = $"Added: {card.Name} (from MPCFill, {SelectedMpcFillCard.Source})";
            }
        }
        catch (Exception ex) { StatusText = $"Download failed: {ex.Message}"; }
        finally { ClearBusy(); }
    }

    private async Task ClearAllCardsAsync()
    {
        if (Cards.Count == 0) return;
        bool confirmed = await _dialogService.ConfirmAsync(
            $"Remove all {Cards.Count} card(s) from the project?", "Clear All Cards");
        if (!confirmed) return;

        PushUndo();
        Cards.Clear();
        ApplyFilterAndSort();
        StatusText = "All cards removed";
    }

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

    // --- Sort and Filter ---

    private void ApplyFilterAndSort()
    {
        var source = Cards.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            string ft = FilterText.Trim();
            source = source.Where(c =>
                c.Name.Contains(ft, StringComparison.OrdinalIgnoreCase) ||
                c.TypeLine.Contains(ft, StringComparison.OrdinalIgnoreCase) ||
                c.OracleText.Contains(ft, StringComparison.OrdinalIgnoreCase) ||
                c.SetName.Contains(ft, StringComparison.OrdinalIgnoreCase) ||
                c.Artist.Contains(ft, StringComparison.OrdinalIgnoreCase) ||
                c.Keywords.Contains(ft, StringComparison.OrdinalIgnoreCase));
        }

        if (FilterRarity != "All")
            source = source.Where(c => c.Rarity.Equals(FilterRarity, StringComparison.OrdinalIgnoreCase));

        if (FilterColor != "All")
        {
            source = FilterColor switch
            {
                "White" => source.Where(c => c.Colors.Contains("W")),
                "Blue" => source.Where(c => c.Colors.Contains("U")),
                "Black" => source.Where(c => c.Colors.Contains("B")),
                "Red" => source.Where(c => c.Colors.Contains("R")),
                "Green" => source.Where(c => c.Colors.Contains("G")),
                "Colorless" => source.Where(c => string.IsNullOrEmpty(c.Colors)),
                "Multicolor" => source.Where(c => c.Colors.Count(ch => ch == ',') >= 1),
                _ => source
            };
        }

        var rarityOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["common"] = 0, ["uncommon"] = 1, ["rare"] = 2, ["mythic"] = 3
        };

        source = SortBy switch
        {
            "Name" => SortDescending ? source.OrderByDescending(c => c.Name) : source.OrderBy(c => c.Name),
            "CMC" => SortDescending ? source.OrderByDescending(c => c.CMC) : source.OrderBy(c => c.CMC),
            "Rarity" => SortDescending
                ? source.OrderByDescending(c => rarityOrder.GetValueOrDefault(c.Rarity, -1))
                : source.OrderBy(c => rarityOrder.GetValueOrDefault(c.Rarity, -1)),
            "Color" => SortDescending ? source.OrderByDescending(c => c.Colors) : source.OrderBy(c => c.Colors),
            "Type" => SortDescending ? source.OrderByDescending(c => c.TypeLine) : source.OrderBy(c => c.TypeLine),
            "Set" => SortDescending
                ? source.OrderByDescending(c => c.SetName).ThenByDescending(c => c.CollectorNumber)
                : source.OrderBy(c => c.SetName).ThenBy(c => c.CollectorNumber),
            "Artist" => SortDescending ? source.OrderByDescending(c => c.Artist) : source.OrderBy(c => c.Artist),
            "Collector #" => SortDescending
                ? source.OrderByDescending(c => c.SetCode).ThenByDescending(c => int.TryParse(c.CollectorNumber, out var n) ? n : 9999)
                : source.OrderBy(c => c.SetCode).ThenBy(c => int.TryParse(c.CollectorNumber, out var n) ? n : 9999),
            _ => SortDescending ? source.OrderByDescending(c => c.DateAdded) : source.OrderBy(c => c.DateAdded),
        };

        FilteredCards = new ObservableCollection<CardModel>(source);
    }

    private void ApplySortToProject()
    {
        if (FilteredCards.Count == 0) return;
        PushUndo();
        var ordered = FilteredCards.ToList();
        var hidden = Cards.Except(ordered).ToList();
        ordered.AddRange(hidden);

        Cards.CollectionChanged -= OnCardsCollectionChanged;
        Cards.Clear();
        foreach (var c in ordered) Cards.Add(c);
        Cards.CollectionChanged += OnCardsCollectionChanged;

        _currentProject.PageSettings.CenterGrid();
        StatusText = $"Project reordered by {SortBy}";
    }

    private void ClearFilter()
    {
        FilterText = string.Empty;
        FilterRarity = "All";
        FilterColor = "All";
        SortBy = "Date Added";
        SortDescending = false;
    }

    // --- Page Layout ---

    private static string DetectPagePreset(PageLayout settings)
    {
        float w = settings.PageWidthMm;
        float h = settings.PageHeightMm;
        float pw = Math.Min(w, h);
        float ph = Math.Max(w, h);

        bool Match(float presetW, float presetH) =>
            Math.Abs(pw - presetW) < 1f && Math.Abs(ph - presetH) < 1f;

        if (Match(210f, 297f)) return "A4";
        if (Match(297f, 420f)) return "A3";
        if (Match(215.9f, 279.4f)) return "Letter";
        if (Match(215.9f, 355.6f)) return "Legal";
        if (Match(279.4f, 431.8f)) return "Tabloid";
        return "A4";
    }

    private void SetPagePreset(string? preset)
    {
        if (string.IsNullOrEmpty(preset)) return;
        _currentProject.PageSettings.ApplyPagePreset(preset);
        StatusText = $"Page size set to {preset}";
    }

    private void ToggleLandscape()
    {
        _currentProject.PageSettings.IsLandscape = !_currentProject.PageSettings.IsLandscape;
        StatusText = _currentProject.PageSettings.IsLandscape ? "Landscape orientation" : "Portrait orientation";
    }

    private void SyncCardsToProject()
    {
        _currentProject.Cards = Cards.ToList();
        _currentProject.LastModified = DateTime.Now;
    }

    private static readonly HashSet<string> BasicLandNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Plains", "Island", "Swamp", "Mountain", "Forest",
        "Snow-Covered Plains", "Snow-Covered Island", "Snow-Covered Swamp",
        "Snow-Covered Mountain", "Snow-Covered Forest", "Wastes"
    };

    private static bool IsBasicLand(string cardName) => BasicLandNames.Contains(cardName);

    private void ApplyDefaultBackArt(CardModel card)
    {
        if (!string.IsNullOrEmpty(card.BackArtworkPath)) return;

        // Yu-Gi-Oh! cards default to the generated Yu-Gi-Oh! back (overridable via the art selector).
        if (card.Source == CardSource.YgoProDeck)
        {
            card.BackArtworkPath = YgoCardBackProvider.GetOrCreate();
            card.IncludeBack = true;
            return;
        }

        var mostCommon = GetMostCommonBackArt();
        if (mostCommon != null) { card.BackArtworkPath = mostCommon; card.IncludeBack = true; return; }

        var defaultPath = _backArtLibraryService.DefaultBackArtPath;
        if (defaultPath != null) { card.BackArtworkPath = defaultPath; card.IncludeBack = true; }
    }

    private async Task ManageBackArtLibraryAsync()
    {
        await _dialogService.ShowBackArtLibraryAsync(_backArtLibraryService, _mpcFillService, _appSettings);
        RefreshBackArtLibrary();
        StatusText = $"Back art library: {_backArtLibraryService.Entries.Count} item(s)";
    }

    private async Task ManageFrontArtLibraryAsync()
    {
        await _dialogService.ShowFrontArtLibraryAsync(
            _frontArtLibraryService, _imageCacheService, _appSettings, _scryfallService);
        StatusText = $"Front art library: {_frontArtLibraryService.Entries.Count} item(s)";
    }

    private async Task ClearCacheAsync()
    {
        var size = _cacheManager.GetTotalCacheSizeBytes();
        bool confirmed = await _dialogService.ConfirmAsync(
            $"Clear all cached files?\n\n" +
            $"This will free {CacheManager.FormatBytes(size)} of disk space.\n" +
            $"Downloaded card images will need to be re-downloaded.\n\n" +
            $"Your projects, back art library, and favorites are not affected.",
            "Clear Cache");

        if (!confirmed) return;

        var (files, bytes) = _cacheManager.ClearAllCaches();
        OnPropertyChanged(nameof(CacheSizeText));
        StatusText = $"Cleared {files} cached file(s), freed {CacheManager.FormatBytes(bytes)}";
    }

    private async Task OpenSettingsAsync()
    {
        string? oldFrontPath = _appSettings.Settings.FrontArtLibraryPath;
        string? oldBackPath = _appSettings.Settings.BackArtLibraryPath;

        await _dialogService.ShowSettingsAsync(_appSettings, MpcSourceManager, _mpcFillService);

        MpcUseFavoritesOnly = _appSettings.Settings.MpcFillUseFavoritesOnly;
        MpcAdvMinDpi = _appSettings.Settings.MpcFillDefaultMinDpi;
        MpcFuzzySearch = _appSettings.Settings.MpcFillDefaultFuzzySearch;

        if (_appSettings.Settings.FrontArtLibraryPath != oldFrontPath)
            _frontArtLibraryService = new FrontArtLibraryService(_appSettings.Settings.FrontArtLibraryPath);
        if (_appSettings.Settings.BackArtLibraryPath != oldBackPath)
        {
            _backArtLibraryService = new BackArtLibraryService(_appSettings.Settings.BackArtLibraryPath);
            RefreshBackArtLibrary();
        }
    }

    private void SetBusy(string message) { BusyMessage = message; StatusText = message; IsBusy = true; }

    private void ClearBusy()
    {
        IsBusy = false;
        BusyMessage = string.Empty;
        BusyIsCancellable = false;
        _busyCts?.Dispose();
        _busyCts = null;
    }

    /// <summary>Starts a busy operation the user can abort (shows a Cancel button). Returns the token
    /// to thread into the work; CancelBusyCommand cancels it.</summary>
    private CancellationToken BeginCancellableBusy(string message)
    {
        _busyCts?.Dispose();
        _busyCts = new CancellationTokenSource();
        BusyIsCancellable = true;
        SetBusy(message);
        return _busyCts.Token;
    }
}
