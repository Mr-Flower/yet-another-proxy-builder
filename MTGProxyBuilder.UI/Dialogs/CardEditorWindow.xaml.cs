using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.UI.ViewModels;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class CardEditorWindow : Window
    {
        private CardEditorViewModel ViewModel => (CardEditorViewModel)DataContext;

        public CardEditorWindow(CardEditorViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();

            // Wire up layer selection to property panel
            viewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Keyboard shortcuts
            InputBindings.Add(new KeyBinding(viewModel.SaveCommand, Key.S, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(viewModel.NewCommand, Key.N, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(viewModel.OpenCommand, Key.O, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(viewModel.DeleteLayerCommand, Key.Delete, ModifierKeys.None));

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

        // --- Routed Event Handlers ---

        private void OnAddImageLayer(object sender, RoutedEventArgs e) => ViewModel.AddImageLayer();
        private void OnAddTextLayer(object sender, RoutedEventArgs e) => ViewModel.AddTextLayer();
        private void OnDeleteLayer(object sender, RoutedEventArgs e) => ViewModel.DeleteLayer();
        private void OnMoveUp(object sender, RoutedEventArgs e) => ViewModel.MoveLayerUp();
        private void OnMoveDown(object sender, RoutedEventArgs e) => ViewModel.MoveLayerDown();

        private void OnBrowseImage(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedLayer is ImageLayer)
            {
                // Reuse the add image flow to pick a new image source
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Image",
                    Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All Files|*.*"
                };

                if (dialog.ShowDialog() == true)
                {
                    var imgLayer = (ImageLayer)ViewModel.SelectedLayer;
                    EditorCanvas.Compositor.InvalidateImage(imgLayer.ImageSource);
                    imgLayer.ImageSource = dialog.FileName;
                    imgLayer.ImageBytes = null;

                    // Update dimensions from new image
                    try
                    {
                        using var bmp = SkiaSharp.SKBitmap.Decode(dialog.FileName);
                        if (bmp != null)
                        {
                            imgLayer.Width = bmp.Width;
                            imgLayer.Height = bmp.Height;
                        }
                    }
                    catch { }

                    ViewModel.NotifyRefresh();
                }
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            if (ViewModel.HasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Save before closing?",
                    "Save Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                switch (result)
                {
                    case MessageBoxResult.Yes:
                        _ = ViewModel.SaveAsync();
                        break;
                    case MessageBoxResult.Cancel:
                        e.Cancel = true;
                        break;
                }
            }
        }
    }
}
