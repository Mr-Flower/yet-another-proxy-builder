using System.IO;
using Avalonia.Controls;

namespace MTGProxyBuilder.UI.Dialogs;

public partial class ImagePreviewWindow : Window
{
    public ImagePreviewWindow(string imagePath, string? title = null)
    {
        InitializeComponent();
        Title = title ?? "Image Preview";
        Loaded += (_, _) =>
        {
            PreviewPanel.ShowImage(imagePath, title ?? Path.GetFileName(imagePath));
        };
    }
}
