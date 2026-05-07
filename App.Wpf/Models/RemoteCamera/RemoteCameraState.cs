namespace TitanAILivePC.Models.RemoteCamera;

public enum RemoteCameraState
{
    Offline,
    WaitingForPhone,
    Pairing,
    Connected,
    Streaming,
    Reconnecting,
    Expired,
    Error,
}
