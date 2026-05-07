using System.Security.Cryptography;
using TitanAILivePC.Models.RemoteCamera;

namespace TitanAILivePC.Services.RemoteCamera;

public sealed class RemoteCameraSessionService
{
    public RemoteCameraSession CreateSession(RemoteCameraSettings settings)
    {
        var roomCode = GenerateRoomCode();
        var token = Guid.NewGuid().ToString("N");
        var webAppBaseUrl = NormalizeBaseUrl(settings.WebAppBaseUrl, "https://titan-webcam.vercel.app");
        var signalingBaseUrl = NormalizeBaseUrl(settings.SignalingServerUrl, "https://titan-camera-server.onrender.com");
        var session = new RemoteCameraSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            RoomCode = roomCode,
            PairingToken = token,
            PairingUrl = $"{webAppBaseUrl}/join?room={roomCode}&token={token}&server={Uri.EscapeDataString(signalingBaseUrl)}",
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
}
