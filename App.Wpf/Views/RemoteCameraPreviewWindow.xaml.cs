using System.Windows;

namespace TitanAILivePC.Views;

public partial class RemoteCameraPreviewWindow : Window
{
    public RemoteCameraPreviewWindow(string previewUrl)
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try
            {
                await PreviewWebView.EnsureCoreWebView2Async();
                PreviewWebView.Source = new Uri(previewUrl);
            }
            catch
            {
                // ignore webview boot errors
            }
        };
    }
}
