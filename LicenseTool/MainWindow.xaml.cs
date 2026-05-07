using System.Windows;

namespace LicenseTool;

public partial class MainWindow : Window
{
    private readonly LicenseGeneratorService _licenseGenerator = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        var hardwareId = HardwareIdTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(hardwareId))
        {
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.OrangeRed;
            StatusTextBlock.Text = "Vui long nhap Hardware ID.";
            return;
        }

        try
        {
            var activationCode = _licenseGenerator.Generate(hardwareId, 365);
            ActivationCodeTextBox.Text = activationCode;
            Clipboard.SetText(activationCode);
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.LightGreen;
            StatusTextBlock.Text = "Da tao va copy Activation Code.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.OrangeRed;
            StatusTextBlock.Text = $"Loi: {ex.Message}";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
