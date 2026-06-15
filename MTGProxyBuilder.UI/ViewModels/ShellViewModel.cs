using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.Services;

namespace MTGProxyBuilder.UI.ViewModels;

public class ShellViewModel : ObservableObject
{
    private ProjectViewModel? _activeProject;
    private readonly AppSettingsService _appSettings;
    private readonly MpcFillSourceManager _mpcSourceManager;
    private readonly ImageCacheService _imageCacheService;
    private readonly ScryfallService _scryfallService;
    private readonly MpcFillService _mpcFillService;
    private BackArtLibraryService _backArtLibraryService;
    private FrontArtLibraryService _frontArtLibraryService;
    private readonly UpdateCheckService _updateService;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _provider;
    private readonly IRelayCommand _closeProjectCmd;
    private readonly IRelayCommand _closeAllProjectsCmd;
    private bool _updateAvailable;
    private string _updateMessage = string.Empty;
    private string _updateDownloadUrl = string.Empty;
    private bool _isLoading;
    private string _loadingMessage = string.Empty;

    public ShellViewModel(IDialogService dialogService, AppServices services, IServiceProvider provider)
    {
        _dialogService = dialogService;
        _provider = provider;
        _appSettings = services.Settings;
        _mpcSourceManager = services.MpcSources;
        _imageCacheService = services.ImageCache;
        _scryfallService = services.Scryfall;
        _mpcFillService = services.MpcFill;
        _updateService = services.UpdateCheck;
        _backArtLibraryService = new BackArtLibraryService(_appSettings.Settings.BackArtLibraryPath);
        _frontArtLibraryService = new FrontArtLibraryService(_appSettings.Settings.FrontArtLibraryPath);
        Projects = new ObservableCollection<ProjectViewModel>();

        _closeProjectCmd = new AsyncRelayCommand(() => CloseActiveProjectAsync(), () => ActiveProject != null);
        _closeAllProjectsCmd = new AsyncRelayCommand(() => CloseAllProjectsAsync(), () => Projects.Count > 0);

        NewProjectCommand = new RelayCommand(() => NewProject());
        OpenProjectCommand = new AsyncRelayCommand(() => OpenProjectAsync());
        CloseProjectCommand = _closeProjectCmd;
        CloseAllProjectsCommand = _closeAllProjectsCmd;
        OpenSettingsCommand = new AsyncRelayCommand(() => OpenSettingsAsync());
        ExitCommand = new RelayCommand(() => _dialogService.Shutdown());
        DownloadUpdateCommand = new RelayCommand(() => DownloadUpdate());
        DismissUpdateCommand = new RelayCommand(() => UpdateAvailable = false);
        ManageFrontArtLibraryCommand = new AsyncRelayCommand(() => ManageFrontArtLibraryAsync());
        ManageBackArtLibraryCommand = new AsyncRelayCommand(() => ManageBackArtLibraryAsync());

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
            _closeProjectCmd.NotifyCanExecuteChanged();
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
        var vm = _provider.GetRequiredService<MainViewModel>();
        vm.UseSharedLibraries(_frontArtLibraryService, _backArtLibraryService);
        ApplyDefaults(vm);
        var tab = new ProjectViewModel(vm);
        Projects.Add(tab);
        ActiveProject = tab;
        _closeAllProjectsCmd.NotifyCanExecuteChanged();
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
            var vm = _provider.GetRequiredService<MainViewModel>();
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
            _closeAllProjectsCmd.NotifyCanExecuteChanged();
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
        _closeAllProjectsCmd.NotifyCanExecuteChanged();

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
        _closeAllProjectsCmd.NotifyCanExecuteChanged();
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
}
