using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.ViewModels
{
    /// <summary>
    /// Top-level ViewModel for the application shell.
    /// Manages the collection of open project tabs and global commands.
    /// </summary>
    public class ShellViewModel : INotifyPropertyChanged
    {
        private ProjectViewModel? _activeProject;
        private readonly AppSettingsService _appSettings = new();
        private readonly MpcFillSourceManager _mpcSourceManager = new();
        private readonly UpdateCheckService _updateService = new();
        private bool _updateAvailable;
        private string _updateMessage = string.Empty;
        private string _updateDownloadUrl = string.Empty;

        public ShellViewModel()
        {
            Projects = new ObservableCollection<ProjectViewModel>();

            NewProjectCommand = new RelayCommand(_ => NewProject());
            OpenProjectCommand = new RelayCommand(_ => OpenProject());
            CloseProjectCommand = new RelayCommand(_ => CloseActiveProject(), _ => ActiveProject != null);
            CloseAllProjectsCommand = new RelayCommand(_ => CloseAllProjects(), _ => Projects.Count > 0);
            OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
            ExitCommand = new RelayCommand(_ => Application.Current.Shutdown());
            DownloadUpdateCommand = new RelayCommand(_ => DownloadUpdate());
            DismissUpdateCommand = new RelayCommand(_ => UpdateAvailable = false);

            _ = CheckForUpdateAsync();
        }

        public ObservableCollection<ProjectViewModel> Projects { get; }

        public ProjectViewModel? ActiveProject
        {
            get => _activeProject;
            set { SetProperty(ref _activeProject, value); OnPropertyChanged(nameof(HasActiveProject)); }
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

        // --- Project Management ---

        public void NewProject()
        {
            var vm = new MainViewModel();
            ApplyDefaults(vm);
            var tab = new ProjectViewModel(vm);
            Projects.Add(tab);
            ActiveProject = tab;
        }

        private void ApplyDefaults(MainViewModel vm)
        {
            var s = _appSettings.Settings;

            vm.CurrentProject.PageSettings.BleedWidthMm = s.DefaultBleedMm;

            // Set via ViewModel properties so both the model AND the ComboBox are updated
            vm.SelectedPagePreset = s.DefaultPagePreset;

            var preset = CardSizePreset.BuiltInPresets.FirstOrDefault(
                p => p.Name == s.DefaultCardSizePreset);
            if (preset != null)
                vm.SelectedCardSize = preset;

            vm.HasUnsavedChanges = false;
        }

        public async void OpenProject()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "MTG Project Files (*.mtgproj)|*.mtgproj|All Files (*.*)|*.*",
                Title = "Open Project"
            };
            if (dialog.ShowDialog() != true) return;

            // Check if already open
            foreach (var p in Projects)
            {
                if (string.Equals(p.FilePath, dialog.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    ActiveProject = p;
                    return;
                }
            }

            var vm = new MainViewModel();
            var serializer = new ProjectSerializationService();
            var project = await serializer.LoadProjectAsync(dialog.FileName);
            if (project == null)
            {
                MessageBox.Show("Failed to load project file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            vm.LoadFromProject(project, dialog.FileName);
            var tab = new ProjectViewModel(vm) { FilePath = dialog.FileName };
            Projects.Add(tab);
            ActiveProject = tab;
        }

        public void CloseActiveProject()
        {
            if (ActiveProject == null) return;
            CloseProject(ActiveProject);
        }

        public void CloseProject(ProjectViewModel project)
        {
            if (project.HasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    $"Save changes to \"{project.Inner.ProjectName}\"?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    if (project.Inner.SaveProjectCommand.CanExecute(null))
                        project.Inner.SaveProjectCommand.Execute(null);
                }
                else if (result == MessageBoxResult.Cancel)
                    return;
            }

            int idx = Projects.IndexOf(project);
            Projects.Remove(project);

            if (ActiveProject == project)
            {
                if (Projects.Count > 0)
                    ActiveProject = Projects[Math.Min(idx, Projects.Count - 1)];
                else
                    ActiveProject = null;
            }
        }

        public void CloseAllProjects()
        {
            for (int i = Projects.Count - 1; i >= 0; i--)
            {
                var p = Projects[i];
                if (p.HasUnsavedChanges)
                {
                    var result = MessageBox.Show(
                        $"Save changes to \"{p.Inner.ProjectName}\"?",
                        "Unsaved Changes",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        if (p.Inner.SaveProjectCommand.CanExecute(null))
                            p.Inner.SaveProjectCommand.Execute(null);
                    }
                    else if (result == MessageBoxResult.Cancel)
                        return;
                }
                Projects.RemoveAt(i);
            }
            ActiveProject = null;
        }

        private void OpenSettings()
        {
            var dialog = new Dialogs.SettingsDialog(_appSettings, _mpcSourceManager);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();
        }

        // --- Update Check ---

        private async System.Threading.Tasks.Task CheckForUpdateAsync()
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

        /// <summary>Check all open projects for unsaved changes. Returns true if safe to close.</summary>
        public bool CanCloseApplication()
        {
            foreach (var p in Projects)
            {
                if (p.HasUnsavedChanges)
                {
                    ActiveProject = p;
                    var result = MessageBox.Show(
                        $"Save changes to \"{p.Inner.ProjectName}\"?",
                        "Unsaved Changes",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        if (p.Inner.SaveProjectCommand.CanExecute(null))
                            p.Inner.SaveProjectCommand.Execute(null);
                    }
                    else if (result == MessageBoxResult.Cancel)
                        return false;
                }
            }
            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
