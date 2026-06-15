using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.ViewModels
{
    /// <summary>
    /// Represents a single open project tab. Wraps a MainViewModel instance
    /// (which contains all per-project state and commands) with tab metadata.
    /// </summary>
    public class ProjectViewModel : ObservableObject
    {
        private string _tabTitle = "Untitled Project";
        private string? _filePath;
        private bool _hasUnsavedChanges;
        private readonly MainViewModel _inner;

        public ProjectViewModel(MainViewModel inner)
        {
            _inner = inner;
            _inner.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.ProjectName))
                    UpdateTabTitle();
                if (e.PropertyName == nameof(MainViewModel.HasUnsavedChanges))
                {
                    _hasUnsavedChanges = _inner.HasUnsavedChanges;
                    UpdateTabTitle();
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                }
            };
            UpdateTabTitle();
        }

        /// <summary>The full MainViewModel that contains all project state and commands.</summary>
        public MainViewModel Inner => _inner;

        public string TabTitle
        {
            get => _tabTitle;
            private set { _tabTitle = value; OnPropertyChanged(); }
        }

        public string? FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); UpdateTabTitle(); }
        }

        public bool HasUnsavedChanges => _inner.HasUnsavedChanges;

        private void UpdateTabTitle()
        {
            string name = _inner.ProjectName;
            if (string.IsNullOrEmpty(name)) name = "Untitled";
            TabTitle = _inner.HasUnsavedChanges ? $"{name} *" : name;
        }

    }
}
