using System.Security.Cryptography;
using TitanAILivePC.Models.RemoteCamera;

namespace TitanAILivePC.Services.RemoteCamera;

public sealed class RemoteCameraSessionService
{
    private const string DefaultWebAppBaseUrl = "https://titan-web-cam.vercel.app";
    private const string DefaultSignalingBaseUrl = "https://titan-camera-server.onrender.com";

    public RemoteCameraSession CreateSession(RemoteCameraSettings settings)
    {
        var roomCode = GenerateRoomCode();
        var token = Guid.NewGuid().ToString("N");
        var signalingBaseUrl = NormalizeBaseUrl(settings.SignalingServerUrl, DefaultSignalingBaseUrl);
        var webAppBaseUrl = ResolveWebAppBaseUrl(settings.WebAppBaseUrl, signalingBaseUrl);
        var pairingUrl =
            $"{webAppBaseUrl}/join?room={Uri.EscapeDataString(roomCode)}" +
            $"&token={Uri.EscapeDataString(token)}";
        var session = new RemoteCameraSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            RoomCode = roomCode,
            PairingToken = token,
            PairingUrl = pairingUrl,
            State = RemoteCameraState.WaitingForPhone,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
        };

        return session;
    }

    public RemoteCameraSession Disconnect(RemoteCameraSession current)
    {
        current.State = RemoteCameraState.Offline;
        current.ConnectedDevice = null;
        return current;
    }

    private static string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> buffer = stackalloc char[6];
        Span<byte> randomBytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(randomBytes);

        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = chars[randomBytes[i] % chars.Length];
        }

        return new string(buffer);
    }

    private static string NormalizeBaseUrl(string url, string fallback)
    {
        var raw = string.IsNullOrWhiteSpace(url) ? fallback : url.Trim();
        return raw.TrimEnd('/');
    }

    private static string ResolveWebAppBaseUrl(string webAppUrl, string signalingBaseUrl)
    {
        var normalizedWebApp = NormalizeBaseUrl(webAppUrl, DefaultWebAppBaseUrl);
        if (LooksLikeSignalingHost(normalizedWebApp, signalingBaseUrl))
        {
            return DefaultWebAppBaseUrl;
        }

        return normalizedWebApp;
    }

    private static bool LooksLikeSignalingHost(string webAppUrl, string signalingBaseUrl)
    {
        if (string.Equals(webAppUrl, signalingBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Uri.TryCreate(webAppUrl, UriKind.Absolute, out var webUri))
        {
            if (webUri.Host.Contains("titan-camera-server", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (Uri.TryCreate(signalingBaseUrl, UriKind.Absolute, out var signalUri) &&
                string.Equals(webUri.Host, signalUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
