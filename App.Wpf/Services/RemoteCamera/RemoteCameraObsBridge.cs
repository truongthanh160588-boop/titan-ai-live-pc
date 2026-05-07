namespace TitanAILivePC.Services.RemoteCamera;

public sealed class RemoteCameraObsBridge
{
    public Task<string> AddToObsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("Phase 1: Da tao cau hinh Browser Source 'Titan Remote Camera' (mock).");
    }

    public Task<string> ShowInObsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("Phase 1: Da gui lenh SHOW CAMERA IN OBS (mock).");
    }
}
