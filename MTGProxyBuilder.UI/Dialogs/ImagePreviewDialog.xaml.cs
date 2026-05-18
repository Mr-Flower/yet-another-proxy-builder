using System.Windows;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class ImagePreviewDialog : Window
    {
        public ImagePreviewDialog(string imagePath, string? title = null)
        {
            InitializeComponent();
            Title = title ?? "Image Preview";
            Loaded += (_, _) =>
            {
                PreviewPanel.ShowImage(imagePath, title ?? System.IO.Path.GetFileName(imagePath));
            };
        }
    }
}
