using TitanAILivePC.Models.RemoteCamera;

namespace TitanAILivePC.Services.RemoteCamera;

public sealed class RemoteCameraWebRtcReceiver
{
    public string BuildPreviewUrl(RemoteCameraSession session, RemoteCameraSettings settings)
    {
        var baseUrl = settings.SignalingServerUrl.TrimEnd('/');
        var query = $"room={Uri.EscapeDataString(session.RoomCode)}&token={Uri.EscapeDataString(session.PairingToken)}" +
                    $"&stun={Uri.EscapeDataString(settings.StunServerUrl)}" +
                    $"&turn={Uri.EscapeDataString(settings.TurnServerUrl)}" +
                    $"&turnUser={Uri.EscapeDataString(settings.TurnUsername)}" +
                    $"&turnPass={Uri.EscapeDataString(settings.TurnPassword)}";
        return $"{baseUrl}/pc-preview?{query}";
    }
}
