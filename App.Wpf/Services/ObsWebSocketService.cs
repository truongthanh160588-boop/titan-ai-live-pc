using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

namespace TitanAILivePC.Services;

public sealed class ObsWebSocketService
{
    /// <remarks>Defer native websocket client construction until first connect/query.</remarks>
    private OBSWebsocket? _obs;

    private OBSWebsocket Obs => _obs ??= new OBSWebsocket();

    public bool IsConnected => Obs.IsConnected;

    public async Task<string> ConnectAsync(string host, int port, string password)
    {
        if (IsConnected)
        {
            return "OBS already connected.";
        }

        try
        {
            var url = $"ws://{host}:{port}";
#pragma warning disable CS0618
            await Task.Run(() => Obs.Connect(url, password));
#pragma warning restore CS0618
            return "OBS connected successfully.";
        }
        catch (AuthFailureException)
        {
            return "OBS connection failed: invalid password.";
        }
        catch (ErrorResponseException ex)
        {
            return $"OBS connection failed: {ex.Message}";
        }
        catch (Exception)
        {
            return "OBS connection failed: OBS may not be open or websocket is disabled.";
        }
    }

    public string Disconnect()
    {
        if (!IsConnected)
        {
            return "OBS already disconnected.";
        }

        Obs.Disconnect();
        return "OBS disconnected.";
    }

    public async Task<IReadOnlyList<string>> GetScenesAsync()
    {
        if (!IsConnected)
        {
            return [];
        }

        return await Task.Run(() => Obs.GetSceneList().Scenes.Select(s => s.Name).ToList());
    }

    public async Task<string> GetCurrentProgramSceneAsync()
    {
        if (!IsConnected)
        {
            return string.Empty;
        }

        try
        {
            return await Task.Run(() => Obs.GetCurrentProgramScene());
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task<string> SwitchSceneAsync(string sceneName)
    {
        if (!IsConnected)
        {
            return "OBS not connected.";
        }

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return "Please select a scene.";
        }

        await Task.Run(() => Obs.SetCurrentProgramScene(sceneName));
        return $"Switched to scene: {sceneName}";
    }

    public async Task<IReadOnlyList<string>> GetSourcesInSceneAsync(string sceneName)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(sceneName))
        {
            return [];
        }

        return await Task.Run(() =>
            Obs.GetSceneItemList(sceneName)
                .Select(i => i.SourceName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    public async Task<string> SetSourceVisibilityAsync(string sceneName, string sourceName, bool visible)
    {
        if (!IsConnected)
        {
            return "OBS not connected.";
        }

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return "Please select source.";
        }

        var sceneOrder = new List<string>();
        void AddScene(string? name)
        {
            if (!string.IsNullOrWhiteSpace(name) &&
                !sceneOrder.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                sceneOrder.Add(name);
            }
        }

        // Priority: program scene (what viewer actually sees) -> selected scene -> all scenes.
        string? currentProgramScene = null;
        try
        {
            currentProgramScene = await Task.Run(() => Obs.GetCurrentProgramScene());
        }
        catch
        {
            // fallback below
        }

        AddScene(currentProgramScene);
        AddScene(sceneName);

        var scenes = await GetScenesAsync();
        foreach (var s in scenes)
        {
            AddScene(s);
        }

        foreach (var candidateScene in sceneOrder)
        {
            var items = await Task.Run(() => Obs.GetSceneItemList(candidateScene));
            var target = items.FirstOrDefault(i => i.SourceName.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                continue;
            }

            await Task.Run(() => Obs.SetSceneItemEnabled(candidateScene, target.ItemId, visible));
            return visible
                ? $"Source shown: {sourceName} (scene: {candidateScene})"
                : $"Source hidden: {sourceName} (scene: {candidateScene})";
        }

        return string.IsNullOrWhiteSpace(sceneName) && string.IsNullOrWhiteSpace(currentProgramScene)
            ? $"Source '{sourceName}' not found in any scene."
            : $"Source '{sourceName}' not found in program/selected scenes or any other scene.";
    }

    public async Task<string> UpdateTextSourceAsync(string sourceName, string text)
    {
        if (!IsConnected)
        {
            return "OBS not connected.";
        }

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return "Please enter OBS text source name.";
        }

        await Task.Run(() => Obs.SetInputSettings(sourceName, new JObject { ["text"] = text ?? string.Empty }, true));
        return $"Updated OBS text source: {sourceName}";
    }

    public async Task<(bool IsStreaming, string Status)> GetStreamingStatusAsync()
    {
        if (!IsConnected)
        {
            return (false, "OBS not connected.");
        }

        try
        {
            var streamStatus = await Task.Run(() => Obs.GetStreamStatus());
            return (streamStatus.IsActive, streamStatus.IsActive ? "OBS streaming is live." : "OBS streaming is stopped.");
        }
        catch (Exception ex)
        {
            return (false, $"Unable to read OBS streaming status: {ex.Message}");
        }
    }

    public async Task<string> StartStreamAsync()
    {
        if (!IsConnected)
        {
            return "OBS not connected.";
        }

        try
        {
            await Task.Run(() => Obs.StartStream());
            return "OBS stream started.";
        }
        catch (Exception ex)
        {
            return $"Failed to start OBS stream: {ex.Message}";
        }
    }

    public async Task<string> StopStreamAsync()
    {
        if (!IsConnected)
        {
            return "OBS not connected.";
        }

        try
        {
            await Task.Run(() => Obs.StopStream());
            return "OBS stream stopped.";
        }
        catch (Exception ex)
        {
            return $"Failed to stop OBS stream: {ex.Message}";
        }
    }
}
