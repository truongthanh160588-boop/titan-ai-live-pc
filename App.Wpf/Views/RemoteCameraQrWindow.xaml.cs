using System.Windows;
using System.Windows.Media;

namespace TitanAILivePC.Views;

public partial class RemoteCameraQrWindow : Window
{
    private readonly string _pairingUrl;

    public string RoomCode { get; }
    public string PairingUrl { get; }
    public ImageSource? QrImage { get; }

    public RemoteCameraQrWindow(string roomCode, string pairingUrl, ImageSource? qrImage)
    {
        InitializeComponent();
        RoomCode = roomCode;
        PairingUrl = pairingUrl;
        QrImage = qrImage;
        _pairingUrl = pairingUrl;
        DataContext = this;

        QrImageControl.Source = QrImage;
        QrFallbackText.Visibility = QrImage is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_pairingUrl))
        {
            Clipboard.SetText(_pairingUrl);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
