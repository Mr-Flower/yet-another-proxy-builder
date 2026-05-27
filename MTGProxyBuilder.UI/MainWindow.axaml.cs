using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MTGProxyBuilder.UI.Services;
using MTGProxyBuilder.UI.ViewModels;

namespace MTGProxyBuilder.UI;

public partial class MainWindow : Window
{
    private double _zoom = 1.0;
    private const double ZoomMin = 0.15;
    private const double ZoomMax = 3.0;
    private const double ZoomStep = 0.1;

    private bool _closing;

    private ShellViewModel Shell => (ShellViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel(new AvaloniaDialogService());

        Closing += OnWindowClosing;
        KeyDown += OnKeyDown;

        Opened += (_, _) =>
        {
            ScryfallSearchBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Return && Shell.ActiveProject?.Inner is { } vm
                    && vm.ScryfallSearchCommand.CanExecute(null))
                    vm.ScryfallSearchCommand.Execute(null);
            };

            DeckImportUrlBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Return && Shell.ActiveProject?.Inner is { } vm
                    && vm.ImportDeckCommand.CanExecute(null))
                    vm.ImportDeckCommand.Execute(null);
            };

            // GridCanvas events wired in Phase 4
        };
    }

    // --- Tab bar ---

    private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control c && c.Tag is ProjectViewModel tab)
            Shell.ActiveProject = tab;
    }

    private void OnTabClose(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.Tag is ProjectViewModel tab)
            _ = Shell.CloseProjectAsync(tab);
    }

    // --- Scryfall double-tap ---

    private void OnScryfallDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Shell.ActiveProject?.Inner is { } vm && vm.AddScryfallCardCommand.CanExecute(null))
            vm.AddScryfallCardCommand.Execute(null);
    }

    // --- Keyboard shortcuts ---

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            switch (e.Key)
            {
                case Key.N:
                    Shell.NewProject(); e.Handled = true; return;
                case Key.O:
                    _ = Shell.OpenProjectAsync(); e.Handled = true; return;
                case Key.W:
                    _ = Shell.CloseActiveProjectAsync(); e.Handled = true; return;
            }

            if (Shell.ActiveProject?.Inner is not MainViewModel vm) return;
            switch (e.Key)
            {
                case Key.Z:
                    if (vm.UndoCommand.CanExecute(null)) vm.UndoCommand.Execute(null);
                    e.Handled = true; break;
                case Key.Y:
                    if (vm.RedoCommand.CanExecute(null)) vm.RedoCommand.Execute(null);
                    e.Handled = true; break;
                case Key.S:
                    if (vm.SaveProjectCommand.CanExecute(null)) vm.SaveProjectCommand.Execute(null);
                    e.Handled = true; break;
                case Key.E:
                    if (vm.ExportPdfCommand.CanExecute(null)) vm.ExportPdfCommand.Execute(null);
                    e.Handled = true; break;
            }
        }
    }

    // --- Zoom ---

    private void ZoomIn(object? sender, RoutedEventArgs e) => SetZoom(_zoom + ZoomStep);
    private void ZoomOut(object? sender, RoutedEventArgs e) => SetZoom(_zoom - ZoomStep);
    private void ZoomReset(object? sender, RoutedEventArgs e) => SetZoom(1.0);

    private void ZoomFit(object? sender, RoutedEventArgs e)
    {
        // Canvas fit zoom wired to GridCanvas in Phase 4
        SetZoom(1.0);
    }

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, ZoomMin, ZoomMax);
        ZoomLabel.Text = $"{(int)(_zoom * 100)}%";
        // GridCanvas RenderTransform scale applied in Phase 4
    }

    // --- Window close (async to allow unsaved-changes dialog) ---

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        _ = HandleCloseAsync();
    }

    private async Task HandleCloseAsync()
    {
        if (await Shell.CanCloseApplicationAsync())
        {
            _closing = true;
            Close();
        }
    }
}
