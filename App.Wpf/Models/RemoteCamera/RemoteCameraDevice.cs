namespace TitanAILivePC.Models.RemoteCamera;

public sealed class RemoteCameraDevice
{
    public string DeviceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Titan WebCam Device";
    public string RoomCode { get; set; } = string.Empty;
    public bool IsVideoEnabled { get; set; } = true;
    public bool IsAudioEnabled { get; set; } = false;
    public string VideoResolution { get; set; } = "1280x720";
    public int FrameRate { get; set; } = 30;
    public int BitrateKbps { get; set; } = 2500;
    public string NetworkType { get; set; } = "Unknown";
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}
