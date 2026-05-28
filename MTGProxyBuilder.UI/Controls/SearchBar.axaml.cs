using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MTGProxyBuilder.UI.Controls
{
    public partial class SearchBar : UserControl
    {
        public SearchBar()
        {
            InitializeComponent();
        }

        // ================================================================
        //  EVENTS
        // ================================================================

        /// <summary>Fired when the user presses Enter or clicks the Search button.</summary>
        public event EventHandler? SearchRequested;

        /// <summary>Fired when the source filter dropdown selection changes.</summary>
        public event EventHandler? SourceChanged;

        // ================================================================
        //  STYLED PROPERTIES
        // ================================================================

        public static readonly StyledProperty<string> PlaceholderProperty =
            AvaloniaProperty.Register<SearchBar, string>(nameof(Placeholder), defaultValue: "Search...");

        public string Placeholder
        {
            get => GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        public static readonly StyledProperty<bool> ShowSourceFilterProperty =
            AvaloniaProperty.Register<SearchBar, bool>(nameof(ShowSourceFilter), defaultValue: true);

        public bool ShowSourceFilter
        {
            get => GetValue(ShowSourceFilterProperty);
            set => SetValue(ShowSourceFilterProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == PlaceholderProperty && PlaceholderBlock != null)
            {
                PlaceholderBlock.Text = (string)change.NewValue!;
            }
            else if (change.Property == ShowSourceFilterProperty && SourceCombo != null)
            {
                SourceCombo.IsVisible = (bool)change.NewValue!;
            }
        }

        // ================================================================
        //  PUBLIC API
        // ================================================================

        public string SearchText => SearchTextBox.Text?.Trim() ?? "";

        public string SelectedSource
        {
            get
            {
                if (SourceCombo.SelectedItem is string s) return s;
                if (SourceCombo.SelectedItem is ComboBoxItem item) return item.Tag?.ToString() ?? item.Content?.ToString() ?? "";
                return "";
            }
        }

        /// <summary>Populate the source filter dropdown with a list of source names. Includes "All" as the first item.</summary>
        public void SetSources(IEnumerable<string> sources, string allLabel = "All Sources")
        {
            SourceCombo.SelectionChanged -= OnSourceSelectionChanged;
            string current = SelectedSource;

            SourceCombo.Items.Clear();
            SourceCombo.Items.Add(allLabel);
            SourceCombo.SelectedIndex = 0;

            foreach (var s in sources)
            {
                SourceCombo.Items.Add(s);
                if (s.Equals(current, StringComparison.OrdinalIgnoreCase))
                    SourceCombo.SelectedItem = s;
            }

            SourceCombo.SelectionChanged += OnSourceSelectionChanged;
        }

        /// <summary>Returns true if "All" is selected in the source filter.</summary>
        public bool IsAllSourcesSelected => SourceCombo.SelectedIndex == 0;

        /// <summary>Clears the search text.</summary>
        public void Clear()
        {
            SearchTextBox.Text = string.Empty;
        }

        // ================================================================
        //  EVENT HANDLERS
        // ================================================================

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
                SearchRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnTextChanged(object? sender, TextChangedEventArgs e)
        {
            PlaceholderBlock.IsVisible = string.IsNullOrEmpty(SearchTextBox.Text);
        }

        private void OnSearchButtonClick(object? sender, RoutedEventArgs e)
        {
            SearchRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnSourceSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            SourceChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
