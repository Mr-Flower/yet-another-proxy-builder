using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.UI.Controls;
using MTGProxyBuilder.UI.Services;
using MTGProxyBuilder.UI.ViewModels;

namespace MTGProxyBuilder.UI;

public partial class MainWindow : Window
{
    private double _zoom = 1.0;
    private const double ZoomMin  = 0.15;
    private const double ZoomMax  = 3.0;
    private const double ZoomStep = 0.1;

    private bool _closing;

    private ShellViewModel Shell => (ShellViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<ShellViewModel>(App.Services);

        Closing += OnWindowClosing;
        KeyDown += OnKeyDown;

        Opened += (_, _) =>
        {
            DeckImportUrlBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Return && Shell.ActiveProject?.Inner is { } vm
                    && vm.ImportDeckCommand.CanExecute(null))
                    vm.ImportDeckCommand.Execute(null);
            };

            WireCanvasEvents();
        };
    }

    // ================================================================
    //  CANVAS EVENTS
    // ================================================================

    private void WireCanvasEvents()
    {
        GridCanvas.CardDoubleClicked += (card, isBack) =>
        {
            if (Shell.ActiveProject?.Inner is not MainViewModel vm) return;
            var idx = vm.Cards.IndexOf(card);
            if (idx < 0) return;
            var indices = new List<int> { idx };
            if (isBack) vm.SelectBackArtForCards(indices);
            else        vm.SelectFrontArtForCards(indices);
        };

        GridCanvas.SelectFrontArtRequested += cardIndices =>
        {
            if (Shell.ActiveProject?.Inner is not MainViewModel vm) return;
            vm.SelectFrontArtForCards(cardIndices);
        };

        GridCanvas.SelectBackArtRequested += cardIndices =>
        {
            if (Shell.ActiveProject?.Inner is not MainViewModel vm) return;
            vm.SelectBackArtForCards(cardIndices);
        };

        GridCanvas.ApplyMajorityBackRequested += cardIndices =>
        {
            if (Shell.ActiveProject?.Inner is not MainViewModel vm) return;
            vm.ApplyMajorityBackToCards(cardIndices);
        };

        GridCanvas.CreateTokenRequested += card =>
        {
            if (Shell.ActiveProject?.Inner is not MainViewModel vm) return;
            vm.CreateTokenFromCard(card);
        };

        GridCanvas.CreateTokensFromCardsRequested += cards =>
        {
            if (Shell.ActiveProject?.Inner is not MainViewModel vm) return;
            vm.CreateTokensFromCards(cards);
        };
    }

    // ================================================================
    //  TAB BAR
    // ================================================================

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

    // ================================================================
    //  KEYBOARD SHORTCUTS
    // ================================================================

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            switch (e.Key)
            {
                case Key.N: Shell.NewProject();             e.Handled = true; return;
                case Key.O: _ = Shell.OpenProjectAsync();   e.Handled = true; return;
                case Key.W: _ = Shell.CloseActiveProjectAsync(); e.Handled = true; return;
            }

            if (Shell.ActiveProject?.Inner is not MainViewModel vm) return;
            switch (e.Key)
            {
                case Key.Z: if (vm.UndoCommand.CanExecute(null))          vm.UndoCommand.Execute(null);          e.Handled = true; break;
                case Key.Y: if (vm.RedoCommand.CanExecute(null))          vm.RedoCommand.Execute(null);          e.Handled = true; break;
                case Key.S: if (vm.SaveProjectCommand.CanExecute(null))   vm.SaveProjectCommand.Execute(null);   e.Handled = true; break;
                case Key.E: if (vm.ExportPdfCommand.CanExecute(null))     vm.ExportPdfCommand.Execute(null);     e.Handled = true; break;
            }
        }
    }

    // ================================================================
    //  ZOOM
    // ================================================================

    private void ZoomIn(object? sender, RoutedEventArgs e)  => SetZoom(_zoom + ZoomStep);
    private void ZoomOut(object? sender, RoutedEventArgs e) => SetZoom(_zoom - ZoomStep);
    private void ZoomReset(object? sender, RoutedEventArgs e) => SetZoom(1.0);

    private void ZoomFit(object? sender, RoutedEventArgs e)
    {
        // Natural page width at zoom=1 is GridCanvas.Width / current_zoom.
        // Compute zoom so the page fills the scroll-viewer width (minus padding).
        double naturalW = GridCanvas.Width / _zoom;
        double available = CanvasScrollViewer.Bounds.Width - 70; // subtract padding
        if (naturalW > 0 && available > 0)
            SetZoom(available / naturalW);
    }

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, ZoomMin, ZoomMax);
        ZoomLabel.Text = $"{(int)(_zoom * 100)}%";
        GridCanvas.ZoomLevel = _zoom;
    }

    // ================================================================
    //  WINDOW CLOSE
    // ================================================================

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
