namespace TitanAILivePC.Models.RemoteCamera;

public sealed class RemoteCameraSettings
{
    public string SignalingServerUrl { get; set; } = "https://titan-camera-server.onrender.com";
    public string WebAppBaseUrl { get; set; } = "https://titan-webcam.vercel.app";
    public string StunServerUrl { get; set; } = "stun:stun.l.google.com:19302";
    public string TurnServerUrl { get; set; } = string.Empty;
    public string TurnUsername { get; set; } = string.Empty;
    public string TurnPassword { get; set; } = string.Empty;
    public string PreferredResolution { get; set; } = "1280x720";
    public int PreferredFps { get; set; } = 30;
    public int PreferredBitrateKbps { get; set; } = 2500;
    public bool EnableRemoteAudio { get; set; }
}
