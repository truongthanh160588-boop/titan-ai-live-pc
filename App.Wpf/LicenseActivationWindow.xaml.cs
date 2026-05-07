using System.Windows;
using TitanAILivePC.Services;

namespace TitanAILivePC;

public partial class LicenseActivationWindow : Window
{
    private readonly LicenseService _licenseService;

    public LicenseActivationWindow(LicenseService licenseService)
    {
        InitializeComponent();
        _licenseService = licenseService;
        HardwareIdText.Text = _licenseService.GetHardwareId();
    }

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        if (_licenseService.TryActivate(ActivationCodeText.Text, out var error))
        {
            DialogResult = true;
            Close();
            return;
        }

        StatusText.Text = error;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CopyHardwareId_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(HardwareIdText.Text);
        StatusText.Text = "Đã copy Hardware ID.";
        StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
    }
}
