namespace TitanAILivePC.Models.RemoteCamera;

public sealed class RemoteCameraSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string RoomCode { get; set; } = string.Empty;
    public string PairingToken { get; set; } = string.Empty;
    public string PairingUrl { get; set; } = string.Empty;
    public string QrPayload { get; set; } = string.Empty;
    public RemoteCameraState State { get; set; } = RemoteCameraState.Offline;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddMinutes(30);
    public int RemainingSeconds => Math.Max(0, (int)Math.Floor((ExpiresAtUtc - DateTime.UtcNow).TotalSeconds));
    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;
    public RemoteCameraDevice? ConnectedDevice { get; set; }
}
