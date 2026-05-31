using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
    // fork-specific: right-side Darken panel state. Force Enabled=true so the sliders
    // actually take effect (the stored default may be disabled on first run, which made
    // "Apply to all" a silent no-op).
    private ImageAdjustmentSettings _darken = WithEnabled(new ImageAdjustmentStore().Default);

    private static ImageAdjustmentSettings WithEnabled(ImageAdjustmentSettings s)
    {
        s.Enabled = true;
        return s;
    }
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
    private readonly RelayCommand _addMpcFillCardCmd;
    private readonly RelayCommand _clearAllCardsCmd;
    private readonly RelayCommand _updateAllArtFromMpcFillCmd;
    private readonly RelayCommand _undoCmd;
    private readonly RelayCommand _redoCmd;

    public MainViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;
        _imageCacheService = new ImageCacheService();
        _serializationService = new ProjectSerializationService();
        _pdfGeneratorService = new PdfGeneratorService();
        _scryfallService = new ScryfallService(_imageCacheService);
        _backArtLibraryService = new BackArtLibraryService(_appSettings.Settings.BackArtLibraryPath);
        _frontArtLibraryService = new FrontArtLibraryService(_appSettings.Settings.FrontArtLibraryPath);
        _moxfieldService = new MoxfieldService();
        _archidektService = new ArchidektService();
        _deckImportService = new DeckImportService(_moxfieldService, _archidektService);
        MpcSourceManager = new MpcFillSourceManager();
        _mpcFillService = new MpcFillService(_imageCacheService, MpcSourceManager);
        _mpcXmlImportService = new MpcFillXmlImportService(_mpcFillService, _imageCacheService);
        _searchCoordinator = new SearchCoordinator(_scryfallService, _mpcFillService, _appSettings, MpcSourceManager);
        _importCoordinator = new ImportCoordinator(_searchCoordinator, _deckImportService, _mpcXmlImportService, _frontArtLibraryService);
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
        // Always enabled: ApplyDarkenToAll no-ops gracefully with no cards. (RelayCommand has no
        // auto-requery, and this command had no field to RaiseCanExecuteChanged from, so a
        // Cards.Count predicate left the button stuck disabled after cards were added.)
        ApplyDarkenToAllCommand = new RelayCommand(_ => ApplyDarkenToAll()); // fork-specific
        ResetDarkenCommand = new RelayCommand(_ => ResetDarken());
        SaveDarkenDefaultCommand = new RelayCommand(_ => SaveDarkenDefault());

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

        _addMpcFillCardCmd = new RelayCommand(_ => _ = AddMpcFillCardAsync(), _ => SelectedMpcFillCard != null);
        AddMpcFillCardCommand = _addMpcFillCardCmd;
        _clearAllCardsCmd = new RelayCommand(_ => _ = ClearAllCardsAsync(), _ => Cards.Count > 0);
        ClearAllCardsCommand = _clearAllCardsCmd;
        _updateAllArtFromMpcFillCmd = new RelayCommand(_ => _ = UpdateAllArtFromMpcFillAsync(), _ => Cards.Count > 0);
        UpdateAllArtFromMpcFillCommand = _updateAllArtFromMpcFillCmd;

        PrintModeValues = new ObservableCollection<PrintMode>(Enum.GetValues<PrintMode>());
        PagePresets = new ObservableCollection<string> { "A4", "A3", "Letter", "Legal", "Tabloid" };
        _selectedPagePreset = "A4";
        _selectedCardSize = CardSizePresets.First(p => p.Name == "Magic: The Gathering");

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

    private async void OpenSettings_Impl()
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
        _updateAllArtFromMpcFillCmd.RaiseCanExecuteChanged();
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
    public ICommand UpdateAllArtFromMpcFillCommand { get; }

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
    public ICommand ApplyDarkenToAllCommand { get; } // fork-specific: right-side Darken panel
    public ICommand ResetDarkenCommand { get; }      // fork-specific
    public ICommand SaveDarkenDefaultCommand { get; } // fork-specific
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

        PushUndo();
        SetBusy($"Loading {paths.Length} image(s)...");
        await Task.Delay(50);

        Cards.CollectionChanged -= OnCardsCollectionChanged;
        foreach (var filePath in paths)
        {
            var card = new CardModel
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                ArtworkPath = filePath
            };
            ApplyDefaultBackArt(card);
            Cards.Add(card);
        }
        Cards.CollectionChanged += OnCardsCollectionChanged;
        _currentProject.PageSettings.CenterGrid();
        ApplyFilterAndSort();
        StatusText = $"Added {paths.Length} card(s)";
        ClearBusy();
    }

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
            BuildMpcFillSearchOptions(), _frontArtLibraryService);

        if (result != null)
        {
            PushUndo();
            foreach (var c in targets) c.ArtworkPath = result.ResultPath;
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
            BuildMpcFillSearchOptions(), _frontArtLibraryService);

        if (result != null)
        {
            PushUndo();
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
                "Nessuna carta valida selezionata per la generazione token.",
                "Nessun token");
            return;
        }

        SetBusy("Ricerca token associati...");
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
                    "Le carte selezionate non hanno token associati su Scryfall.",
                    "Nessun token");
                return;
            }

            PushUndo();
            foreach (var t in newTokens)
                Cards.Add(t);

            ApplyFilterAndSort();
            StatusText = $"Creati {newTokens.Count} token.";
        }
        catch (Exception ex)
        {
            StatusText = $"Generazione token fallita: {ex.Message}";
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
            BuildMpcFillSearchOptions(), _frontArtLibraryService);

        if (result == null) return;

        PushUndo();
        if (mode == ArtSelectorMode.Front)
        {
            if (result.ApplyToSameName)
            {
                int count = 0;
                foreach (var c in Cards.Where(c => c.Name == card.Name))
                {
                    c.ArtworkPath = result.ResultPath;
                    count++;
                }
                StatusText = $"Front art updated for {count} \"{card.Name}\" card(s)";
            }
            else
            {
                card.ArtworkPath = result.ResultPath;
                StatusText = $"Front art updated for {card.Name}";
            }
        }
        else
        {
            if (result.ApplyToNoBack)
            {
                int count = 0;
                foreach (var c in Cards.Where(c => string.IsNullOrEmpty(c.BackArtworkPath)))
                {
                    c.BackArtworkPath = result.ResultPath;
                    c.IncludeBack = true;
                    count++;
                }
                StatusText = $"Back art applied to {count} card(s) without back art";
            }
            else
            {
                card.BackArtworkPath = result.ResultPath;
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
            BuildMpcFillSearchOptions(), _frontArtLibraryService);

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
            ApplyFilterAndSort();
            StatusText = $"Added: {card.Name} ({card.SetName})";
        }
        catch (Exception ex)
        {
            StatusText = $"Download failed: {ex.Message}";
        }
        finally { ClearBusy(); }
    }

    // fork-specific: open the image adjustment editor for the selected card and bake the result.
    private async Task AdjustImageAsync()
    {
        var card = SelectedCard;
        if (card == null) return;
        if (string.IsNullOrEmpty(card.ArtworkPath))
        {
            await _dialogService.ShowInfoAsync("La carta selezionata non ha un'immagine fronte.", "Regola immagine");
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
            ImageAdjustmentTarget.AllScryfall => Cards.Where(c => c.Source == CardSource.Scryfall),
            ImageAdjustmentTarget.AllMpcFill  => Cards.Where(c => c.Source == CardSource.MpcFill),
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
            ? (settings.IsNoOp ? "Immagine ripristinata all'originale." : "Regolazione immagine applicata.")
            : $"Regolazione applicata a {n} carta/e.";
    }

    // ===== fork-specific: right-side "Darken Pixels" panel =====
    // Bindable sliders backed by the global default settings (_darken). Changing a
    // slider updates the in-memory state; "Apply to all" bakes it across cards by
    // source; "Save default" persists it for Scryfall auto-apply.

    public bool DarkenEnabled
    {
        get => _darken.Enabled;
        set { _darken.Enabled = value; OnPropertyChanged(); }
    }
    public int DarkenBrightness
    {
        get => _darken.Brightness;
        set { _darken.Brightness = value; OnPropertyChanged(); }
    }
    public int DarkenContrast
    {
        get => _darken.Contrast;
        set { _darken.Contrast = value; OnPropertyChanged(); }
    }
    public int DarkenSaturation
    {
        get => _darken.Saturation;
        set { _darken.Saturation = value; OnPropertyChanged(); }
    }
    public int DarkenBlackPoint
    {
        get => _darken.BlackPoint;
        set { _darken.BlackPoint = value; OnPropertyChanged(); }
    }
    public bool DarkenAutoApplyScryfall
    {
        get => _darken.AutoApplyToScryfall;
        set { _darken.AutoApplyToScryfall = value; OnPropertyChanged(); }
    }

    // "Apply To" target index for the panel (0=All, 1=Scryfall, 2=MPCFill).
    private int _darkenTargetIndex;
    public int DarkenTargetIndex
    {
        get => _darkenTargetIndex;
        set => SetProperty(ref _darkenTargetIndex, value);
    }

    private void ApplyDarkenToAll()
    {
        var settings = _darken.Clone();
        var targets = DarkenTargetIndex switch
        {
            1 => Cards.Where(c => c.Source == CardSource.Scryfall),
            2 => Cards.Where(c => c.Source == CardSource.MpcFill),
            _ => Cards.Where(c => !string.IsNullOrEmpty(c.ArtworkPath))
        };

        PushUndo();
        int n = 0;
        foreach (var c in targets)
        {
            if (string.IsNullOrEmpty(c.ArtworkPath)) continue;
            _imageAdjust.BakeFront(c, settings);
            n++;
        }
        RefreshTrigger++;
        StatusText = settings.IsNoOp
            ? $"Immagini ripristinate all'originale ({n})."
            : $"Darken applicato a {n} carta/e.";
    }

    private void ResetDarken()
    {
        _darken = new ImageAdjustmentSettings { Enabled = true };
        OnPropertyChanged(nameof(DarkenEnabled));
        OnPropertyChanged(nameof(DarkenBrightness));
        OnPropertyChanged(nameof(DarkenContrast));
        OnPropertyChanged(nameof(DarkenSaturation));
        OnPropertyChanged(nameof(DarkenBlackPoint));
        OnPropertyChanged(nameof(DarkenAutoApplyScryfall));
        StatusText = "Darken azzerato.";
    }

    private void SaveDarkenDefault()
    {
        _imageAdjust.OpenStore().SaveDefault(_darken.Clone());
        StatusText = "Darken salvato come predefinito.";
    }

    private async Task ExportPdfAsync()
    {
        var path = await _dialogService.PickSaveFileAsync(
            "Export PDF", "PDF Files (*.pdf)|*.pdf", $"{ProjectName}.pdf");
        if (path == null) return;

        SyncCardsToProject();
        SetBusy("Generating PDF...");
        try
        {
            bool success = await _pdfGeneratorService.GeneratePdfAsync(_currentProject, path);

            if (success)
            {
                string svgInfo = "";
                if (_currentProject.PrintSettings.ExportSvgCutLines)
                {
                    var svgService = new SvgCutLineService();
                    string outputDir = Path.GetDirectoryName(path) ?? ".";
                    string baseName = Path.GetFileNameWithoutExtension(path);
                    var svgFiles = await svgService.GenerateSvgAsync(_currentProject, outputDir, baseName);
                    svgInfo = svgFiles.Count > 0
                        ? $"\n\nSVG cut files ({svgFiles.Count}):\n" + string.Join("\n", svgFiles.Select(Path.GetFileName))
                        : "";
                }

                StatusText = $"PDF exported: {Path.GetFileName(path)}";
                await _dialogService.ShowInfoAsync(
                    $"PDF exported successfully!\n\n{path}{svgInfo}", "Export Complete");
            }
            else
            {
                StatusText = "PDF export failed";
                await _dialogService.ShowErrorAsync(
                    "Failed to generate PDF. Check that card images exist.", "Export Failed");
            }
        }
        catch (Exception ex)
        {
            StatusText = $"PDF export failed: {ex.Message}";
            await _dialogService.ShowErrorAsync($"PDF generation error:\n{ex.Message}", "Export Failed");
        }
        finally { ClearBusy(); }
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

    private async Task UpdateAllArtFromMpcFillAsync()
    {
        if (Cards.Count == 0) return;

        bool confirmed = await _dialogService.ConfirmAsync(
            $"Search MPCFill for matching art for all {Cards.Count} card(s) and replace their front artwork?\n\n" +
            "This will use the first available MPCFill result for each card.",
            "Update All Art from MPCFill");
        if (!confirmed) return;

        PushUndo();
        SetBusy("Updating card art from MPCFill...");
        try
        {
            var (updated, failed) = await _importCoordinator.UpdateAllArtFromMpcFillAsync(
                Cards, MpcAdvMinDpi, MpcFuzzySearch, MpcUseFavoritesOnly,
                onProgress: msg => BusyMessage = msg);

            StatusText = $"Updated {updated} card(s) with MPCFill art" + (failed > 0 ? $", {failed} not found" : "");
            await _dialogService.ShowInfoAsync(
                $"Updated {updated} card(s) with MPCFill art.\n{(failed > 0 ? $"{failed} card(s) had no matching art." : "")}",
                "Update Complete");
        }
        catch (Exception ex) { StatusText = $"Update failed: {ex.Message}"; }
        finally { ClearBusy(); }
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
        SetBusy($"Connecting to {sourceName}...");

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
                onProgress: msg => BusyMessage = msg);

            BusyMessage = $"Adding {result.Cards.Count} cards to project...";
            await Task.Delay(50);

            Cards.CollectionChanged -= OnCardsCollectionChanged;
            foreach (var c in result.Cards) { ApplyDefaultBackArt(c); Cards.Add(c); }
            Cards.CollectionChanged += OnCardsCollectionChanged;

            _currentProject.PageSettings.CenterGrid();
            ApplyFilterAndSort();
            ImportDeckUrl = string.Empty;

            int totalAdded = result.Cards.Sum(c => c.Quantity);
            string summary = $"Imported {result.Cards.Count} unique card(s) ({totalAdded} total) from \"{deck.Name}\" ({sourceName})";
            if (result.SkippedDupes > 0) summary += $"\n{result.SkippedDupes} duplicate(s) skipped";
            if (result.Failed > 0) summary += $"\n{result.Failed} card(s) could not be found on Scryfall";
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

    // fork-specific: import a pasted text decklist (resolves each name via Scryfall/MPCFill).
    private async Task ImportTextListAsync()
    {
        var deck = DeckImportService.ParseTextList(ImportDeckText);
        if (deck.Entries.Count == 0)
        {
            await _dialogService.ShowWarningAsync(
                "Nessuna carta riconosciuta. Incolla una lista con una carta per riga, es.:\n\n" +
                "2 Sol Ring\n1 Counterspell\nLightning Bolt",
                "Lista vuota");
            return;
        }

        SetBusy("Importazione lista incollata...");
        try
        {
            PushUndo();
            var result = await _importCoordinator.ImportDeckCardsAsync(
                deck, Cards, IgnoreDuplicates, UseMpcFill,
                MpcAdvMinDpi, MpcFuzzySearch, MpcUseFavoritesOnly,
                onProgress: msg => BusyMessage = msg);

            Cards.CollectionChanged -= OnCardsCollectionChanged;
            foreach (var c in result.Cards) { ApplyDefaultBackArt(c); Cards.Add(c); }
            Cards.CollectionChanged += OnCardsCollectionChanged;

            _currentProject.PageSettings.CenterGrid();
            ApplyFilterAndSort();
            ImportDeckText = string.Empty;

            int totalAdded = result.Cards.Sum(c => c.Quantity);
            string summary = $"Importate {result.Cards.Count} carte ({totalAdded} totali) dalla lista incollata";
            if (result.Failed > 0) summary += $"\n{result.Failed} carta/e non trovata/e su Scryfall";
            StatusText = summary;
            await _dialogService.ShowInfoAsync(summary, "Import completato");
        }
        catch (Exception ex)
        {
            StatusText = $"Import fallito: {ex.Message}";
            await _dialogService.ShowErrorAsync($"Errore import:\n{ex.Message}", "Errore");
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
    private void ClearBusy() { IsBusy = false; BusyMessage = string.Empty; }
}
