using System.Diagnostics;
using System.Windows;

namespace TitanAILivePC;

public partial class HelpInfoWindow : Window
{
    public HelpInfoWindow()
    {
        InitializeComponent();
    }

    private void Zalo_Click(object sender, RoutedEventArgs e) => OpenUrl("https://zalo.me/0974704444");
    private void WhatsApp_Click(object sender, RoutedEventArgs e) => OpenUrl("https://wa.me/84974704444");
    private void Facebook_Click(object sender, RoutedEventArgs e) => OpenUrl("https://facebook.com");
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // ignored: fallback is no-op
        }
    }
}
