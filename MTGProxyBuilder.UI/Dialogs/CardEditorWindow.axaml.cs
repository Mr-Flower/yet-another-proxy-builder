using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.UI.ViewModels;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class CardEditorWindow : Window
    {
        private CardEditorViewModel ViewModel => (CardEditorViewModel)DataContext!;

        public CardEditorWindow(CardEditorViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();

            // Wire up layer selection to property panel
            viewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Keyboard shortcuts
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.S, KeyModifiers.Control),
                Command = viewModel.SaveCommand
            });
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.N, KeyModifiers.Control),
                Command = viewModel.NewCommand
            });
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.O, KeyModifiers.Control),
                Command = viewModel.OpenCommand
            });
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.Delete),
                Command = viewModel.DeleteLayerCommand
            });

            // Wire up LayerListPanel events
            LayerPanel.AddImageLayerRequested += (_, _) => _ = viewModel.AddImageLayerAsync();
            LayerPanel.AddTextLayerRequested += (_, _) => viewModel.AddTextLayer();
            LayerPanel.DeleteLayerRequested += (_, _) => viewModel.DeleteLayer();
            LayerPanel.MoveUpRequested += (_, _) => viewModel.MoveLayerUp();
            LayerPanel.MoveDownRequested += (_, _) => viewModel.MoveLayerDown();

            // Wire up PropertyPanel browse event
            PropPanel.BrowseImageRequested += OnBrowseImage;

            // Subscribe to canvas layer selection
            EditorCanvas.LayerSelected += (_, layer) =>
            {
                viewModel.SelectedLayer = layer;
            };

            Loaded += (_, _) =>
            {
                PropPanel.UpdateForSelection(viewModel.SelectedLayer);
                EditorCanvas.QueueRedraw();
            };
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CardEditorViewModel.SelectedLayer))
            {
                PropPanel.UpdateForSelection(ViewModel.SelectedLayer);
                EditorCanvas.QueueRedraw();
            }
            else if (e.PropertyName == nameof(CardEditorViewModel.RefreshTrigger))
            {
                EditorCanvas.QueueRedraw();
            }
        }

        private async void OnBrowseImage(object? sender, EventArgs e)
        {
            if (ViewModel.SelectedLayer is not ImageLayer imgLayer) return;

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Image",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Image Files")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp" }
                    },
                    new FilePickerFileType("All Files")
                    {
                        Patterns = new[] { "*.*" }
                    }
                }
            });

            if (files.Count == 0) return;

            var path = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            EditorCanvas.Compositor.InvalidateImage(imgLayer.ImageSource);
            imgLayer.ImageSource = path;
            imgLayer.ImageBytes = null;

            // Update dimensions from new image
            try
            {
                using var bmp = SkiaSharp.SKBitmap.Decode(path);
                if (bmp != null)
                {
                    imgLayer.Width = bmp.Width;
                    imgLayer.Height = bmp.Height;
                }
            }
            catch { }

            ViewModel.NotifyRefresh();
        }

        protected override async void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);

            if (ViewModel.HasUnsavedChanges)
            {
                // Cancel the close so we can show async dialog
                e.Cancel = true;

                var result = await ShowSaveConfirmDialog();

                switch (result)
                {
                    case MessageBoxResult.Yes:
                        await ViewModel.SaveAsync();
                        Close();
                        break;
                    case MessageBoxResult.No:
                        // Force close without further prompt
                        ViewModel.HasUnsavedChanges = false;
                        Close();
                        break;
                    // Cancel: do nothing, window stays open
                }
            }
        }

        private async Task<MessageBoxResult> ShowSaveConfirmDialog()
        {
            var dialog = new Window
            {
                Title = "Save Changes",
                Width = 380,
                Height = 160,
                Background = Avalonia.Media.Brushes.DimGray,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var result = MessageBoxResult.Cancel;

            var panel = new StackPanel { Margin = new Avalonia.Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = "You have unsaved changes. Save before closing?",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Foreground = Avalonia.Media.Brushes.White,
                Margin = new Avalonia.Thickness(0, 0, 0, 16)
            });

            var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };

            var yesBtn = new Button { Content = "Save", Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0, 120, 212)), Foreground = Avalonia.Media.Brushes.White, Padding = new Avalonia.Thickness(16, 6) };
            var noBtn = new Button { Content = "Don't Save", Padding = new Avalonia.Thickness(16, 6) };
            var cancelBtn = new Button { Content = "Cancel", Padding = new Avalonia.Thickness(16, 6) };

            yesBtn.Click += (_, _) => { result = MessageBoxResult.Yes; dialog.Close(); };
            noBtn.Click += (_, _) => { result = MessageBoxResult.No; dialog.Close(); };
            cancelBtn.Click += (_, _) => { result = MessageBoxResult.Cancel; dialog.Close(); };

            buttons.Children.Add(yesBtn);
            buttons.Children.Add(noBtn);
            buttons.Children.Add(cancelBtn);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            await dialog.ShowDialog(this);
            return result;
        }

        private enum MessageBoxResult { Yes, No, Cancel }
    }
}
