using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;
using TitanAILivePC.Models.RemoteCamera;

namespace TitanAILivePC.Services.RemoteCamera;

public sealed class RemoteCameraQrService
{
    private const string DefaultWebAppBaseUrl = "https://titan-web-cam.vercel.app";

    public string BuildQrPayload(RemoteCameraSession session) =>
        string.IsNullOrWhiteSpace(session.PairingUrl)
            ? $"{DefaultWebAppBaseUrl}/join?room={Uri.EscapeDataString(session.RoomCode)}&token={Uri.EscapeDataString(session.PairingToken)}"
            : session.PairingUrl;

    public ImageSource? CreateQrImageSource(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var pngQr = new PngByteQRCode(qrData);
        var bytes = pngQr.GetGraphic(18, [0, 0, 0], [255, 255, 255], drawQuietZones: true);

        var image = new BitmapImage();
        using var stream = new MemoryStream(bytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
