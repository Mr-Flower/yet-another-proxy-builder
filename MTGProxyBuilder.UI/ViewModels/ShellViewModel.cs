using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.Services;

namespace MTGProxyBuilder.UI.ViewModels;

public class ShellViewModel : INotifyPropertyChanged
{
    private ProjectViewModel? _activeProject;
    private readonly AppSettingsService _appSettings = new();
    private readonly MpcFillSourceManager _mpcSourceManager = new();
    private readonly ImageCacheService _imageCacheService;
    private readonly ScryfallService _scryfallService;
    private readonly MpcFillService _mpcFillService;
    private BackArtLibraryService _backArtLibraryService;
    private FrontArtLibraryService _frontArtLibraryService;
    private readonly UpdateCheckService _updateService = new();
    private readonly IDialogService _dialogService;
    private readonly RelayCommand _closeProjectCmd;
    private readonly RelayCommand _closeAllProjectsCmd;
    private bool _updateAvailable;
    private string _updateMessage = string.Empty;
    private string _updateDownloadUrl = string.Empty;
    private bool _isLoading;
    private string _loadingMessage = string.Empty;

    public ShellViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;
        _imageCacheService = new ImageCacheService();
        _scryfallService = new ScryfallService(_imageCacheService);
        _mpcFillService = new MpcFillService(_imageCacheService, _mpcSourceManager);
        _backArtLibraryService = new BackArtLibraryService(_appSettings.Settings.BackArtLibraryPath);
        _frontArtLibraryService = new FrontArtLibraryService(_appSettings.Settings.FrontArtLibraryPath);
        Projects = new ObservableCollection<ProjectViewModel>();

        _closeProjectCmd = new RelayCommand(_ => _ = CloseActiveProjectAsync(), _ => ActiveProject != null);
        _closeAllProjectsCmd = new RelayCommand(_ => _ = CloseAllProjectsAsync(), _ => Projects.Count > 0);

        NewProjectCommand = new RelayCommand(_ => NewProject());
        OpenProjectCommand = new RelayCommand(_ => _ = OpenProjectAsync());
        CloseProjectCommand = _closeProjectCmd;
        CloseAllProjectsCommand = _closeAllProjectsCmd;
        OpenSettingsCommand = new RelayCommand(_ => _ = OpenSettingsAsync());
        ExitCommand = new RelayCommand(_ => _dialogService.Shutdown());
        DownloadUpdateCommand = new RelayCommand(_ => DownloadUpdate());
        DismissUpdateCommand = new RelayCommand(_ => UpdateAvailable = false);
        ManageFrontArtLibraryCommand = new RelayCommand(_ => _ = ManageFrontArtLibraryAsync());
        ManageBackArtLibraryCommand = new RelayCommand(_ => _ = ManageBackArtLibraryAsync());

        _ = CheckForUpdateAsync();
    }

    public ObservableCollection<ProjectViewModel> Projects { get; }

    public ProjectViewModel? ActiveProject
    {
        get => _activeProject;
        set
        {
            SetProperty(ref _activeProject, value);
            OnPropertyChanged(nameof(HasActiveProject));
            _closeProjectCmd.RaiseCanExecuteChanged();
        }
    }

    public bool HasActiveProject => _activeProject != null;

    // --- Global Commands ---
    public ICommand NewProjectCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand CloseProjectCommand { get; }
    public ICommand CloseAllProjectsCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand DownloadUpdateCommand { get; }
    public ICommand DismissUpdateCommand { get; }
    public ICommand ManageFrontArtLibraryCommand { get; }
    public ICommand ManageBackArtLibraryCommand { get; }

    // --- Update ---
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

    public string AppVersion => MainViewModel.GetAppVersion();

    // --- Loading ---
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string LoadingMessage
    {
        get => _loadingMessage;
        set => SetProperty(ref _loadingMessage, value);
    }

    // --- Project Management ---

    public void NewProject()
    {
        var vm = new MainViewModel(_dialogService);
        vm.UseSharedLibraries(_frontArtLibraryService, _backArtLibraryService);
        ApplyDefaults(vm);
        var tab = new ProjectViewModel(vm);
        Projects.Add(tab);
        ActiveProject = tab;
        _closeAllProjectsCmd.RaiseCanExecuteChanged();
    }

    private void ApplyDefaults(MainViewModel vm)
    {
        var s = _appSettings.Settings;
        vm.CurrentProject.PageSettings.BleedWidthMm = s.DefaultBleedMm;
        vm.SelectedPagePreset = s.DefaultPagePreset;
        var preset = CardSizePreset.BuiltInPresets.FirstOrDefault(p => p.Name == s.DefaultCardSizePreset);
        if (preset != null)
            vm.SelectedCardSize = preset;
        vm.HasUnsavedChanges = false;
    }

    public async Task OpenProjectAsync()
    {
        var path = await _dialogService.PickOpenFileAsync(
            "Open Project", "MTG Project Files|*.mtgproj|All Files|*.*");
        if (path == null) return;

        foreach (var p in Projects)
        {
            if (string.Equals(p.FilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                ActiveProject = p;
                return;
            }
        }

        IsLoading = true;
        LoadingMessage = "Opening project...";
        try
        {
            var vm = new MainViewModel(_dialogService);
            vm.UseSharedLibraries(_frontArtLibraryService, _backArtLibraryService);
            var serializer = new ProjectSerializationService();
            var project = await serializer.LoadProjectAsync(path,
                msg => Dispatcher.UIThread.Post(() => LoadingMessage = msg));
            if (project == null)
            {
                await _dialogService.ShowErrorAsync("Failed to load project file.", "Error");
                return;
            }

            LoadingMessage = "Building project view...";
            vm.LoadFromProject(project, path);
            var tab = new ProjectViewModel(vm) { FilePath = path };
            Projects.Add(tab);
            ActiveProject = tab;
            _closeAllProjectsCmd.RaiseCanExecuteChanged();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public Task CloseActiveProjectAsync()
    {
        if (ActiveProject == null) return Task.CompletedTask;
        return CloseProjectAsync(ActiveProject);
    }

    public async Task CloseProjectAsync(ProjectViewModel project)
    {
        if (project.HasUnsavedChanges)
        {
            var result = await _dialogService.ConfirmCancelAsync(
                $"Save changes to \"{project.Inner.ProjectName}\"?",
                "Unsaved Changes");

            if (result == MessageResult.Yes)
            {
                if (project.Inner.SaveProjectCommand.CanExecute(null))
                    project.Inner.SaveProjectCommand.Execute(null);
            }
            else if (result == MessageResult.Cancel)
                return;
        }

        int idx = Projects.IndexOf(project);
        Projects.Remove(project);
        _closeAllProjectsCmd.RaiseCanExecuteChanged();

        if (ActiveProject == project)
        {
            ActiveProject = Projects.Count > 0
                ? Projects[Math.Min(idx, Projects.Count - 1)]
                : null;
        }
    }

    public async Task CloseAllProjectsAsync()
    {
        for (int i = Projects.Count - 1; i >= 0; i--)
        {
            var p = Projects[i];
            if (p.HasUnsavedChanges)
            {
                var result = await _dialogService.ConfirmCancelAsync(
                    $"Save changes to \"{p.Inner.ProjectName}\"?",
                    "Unsaved Changes");

                if (result == MessageResult.Yes)
                {
                    if (p.Inner.SaveProjectCommand.CanExecute(null))
                        p.Inner.SaveProjectCommand.Execute(null);
                }
                else if (result == MessageResult.Cancel)
                    return;
            }
            Projects.RemoveAt(i);
        }
        ActiveProject = null;
        _closeAllProjectsCmd.RaiseCanExecuteChanged();
    }

    private async Task OpenSettingsAsync()
    {
        string? oldFrontPath = _appSettings.Settings.FrontArtLibraryPath;
        string? oldBackPath = _appSettings.Settings.BackArtLibraryPath;

        await _dialogService.ShowSettingsAsync(_appSettings, _mpcSourceManager, _mpcFillService);

        bool changed = false;
        if (_appSettings.Settings.FrontArtLibraryPath != oldFrontPath)
        {
            _frontArtLibraryService = new FrontArtLibraryService(_appSettings.Settings.FrontArtLibraryPath);
            changed = true;
        }
        if (_appSettings.Settings.BackArtLibraryPath != oldBackPath)
        {
            _backArtLibraryService = new BackArtLibraryService(_appSettings.Settings.BackArtLibraryPath);
            changed = true;
        }
        if (changed)
        {
            foreach (var p in Projects)
                p.Inner.UseSharedLibraries(_frontArtLibraryService, _backArtLibraryService);
        }
    }

    private Task ManageFrontArtLibraryAsync()
        => _dialogService.ShowFrontArtLibraryAsync(
            _frontArtLibraryService, _imageCacheService, _appSettings, _scryfallService);

    private Task ManageBackArtLibraryAsync()
        => _dialogService.ShowBackArtLibraryAsync(
            _backArtLibraryService, _mpcFillService, _appSettings);

    // --- Update Check ---

    private async Task CheckForUpdateAsync()
    {
        try
        {
            if (!_appSettings.Settings.CheckForUpdates) return;

            string version = MainViewModel.GetAppVersion();
            var update = await _updateService.CheckForUpdateAsync(version);
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
        if (string.IsNullOrEmpty(UpdateDownloadUrl)) return;
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

    public async Task<bool> CanCloseApplicationAsync()
    {
        foreach (var p in Projects)
        {
            if (p.HasUnsavedChanges)
            {
                ActiveProject = p;
                var result = await _dialogService.ConfirmCancelAsync(
                    $"Save changes to \"{p.Inner.ProjectName}\"?",
                    "Unsaved Changes");

                if (result == MessageResult.Yes)
                {
                    if (p.Inner.SaveProjectCommand.CanExecute(null))
                        p.Inner.SaveProjectCommand.Execute(null);
                }
                else if (result == MessageResult.Cancel)
                    return false;
            }
        }
        return true;
    }

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
