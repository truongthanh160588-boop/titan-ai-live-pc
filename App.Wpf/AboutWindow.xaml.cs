using System.Reflection;
using System.Windows;

namespace TitanAILivePC;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        VersionText.Text = $"Version {version}";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
