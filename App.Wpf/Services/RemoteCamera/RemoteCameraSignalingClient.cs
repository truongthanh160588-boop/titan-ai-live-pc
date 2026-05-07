using System.Net.WebSockets;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TitanAILivePC.Models.RemoteCamera;

namespace TitanAILivePC.Services.RemoteCamera;

public sealed class RemoteCameraSignalingClient
{
    private readonly HttpClient _httpClient = new();
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCts;
    public bool IsSocketOpen => _socket is { State: WebSocketState.Open };

    public async Task<bool> CreateRoomAsync(RemoteCameraSession session, RemoteCameraSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var endpoint = $"{settings.SignalingServerUrl.TrimEnd('/')}/api/rooms";
        var payload = JsonSerializer.Serialize(new { roomCode = session.RoomCode, token = session.PairingToken });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("joinUrl", out var joinUrl) && joinUrl.ValueKind == JsonValueKind.String)
        {
            var parsed = joinUrl.GetString();
            if (!string.IsNullOrWhiteSpace(parsed))
            {
                session.PairingUrl = parsed;
            }
        }

        return true;
    }

    public async Task ConnectPcAsync(
        RemoteCameraSession session,
        RemoteCameraSettings settings,
        Action<RemoteSignalMessage> onSignalMessage,
        CancellationToken cancellationToken = default)
    {
        await DisconnectAsync();

        _socket = new ClientWebSocket();
        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var wsUrl = BuildWebSocketUrl(settings.SignalingServerUrl, session.RoomCode, session.PairingToken, "pc");
        await _socket.ConnectAsync(new Uri(wsUrl), cancellationToken);
        await SendJsonAsync(new { type = "hello", role = "pc", room = session.RoomCode }, cancellationToken);
        _ = ReceiveLoopAsync(_socket, onSignalMessage, _receiveCts.Token);
    }

    public Task SendHeartbeatAsync(CancellationToken cancellationToken = default) =>
        SendJsonAsync(new { type = "heartbeat", role = "pc" }, cancellationToken);

    public async Task<RoomSignalSnapshot?> GetRoomSnapshotAsync(string roomCode, RemoteCameraSettings settings, CancellationToken cancellationToken = default)
    {
        var endpoint = $"{settings.SignalingServerUrl.TrimEnd('/')}/api/rooms/{Uri.EscapeDataString(roomCode)}";
        using var response = await _httpClient.GetAsync(endpoint, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<RoomSignalSnapshot>(body, options);
    }

    public async Task<bool> HealthCheckAsync(string signalingServerUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(signalingServerUrl))
        {
            return false;
        }

        var endpoint = $"{signalingServerUrl.TrimEnd('/')}/health";
        using var response = await _httpClient.GetAsync(endpoint, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        return true;
    }

    public async Task DisconnectAsync()
    {
        try
        {
            _receiveCts?.Cancel();
            if (_socket is { State: WebSocketState.Open })
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", CancellationToken.None);
            }
        }
        catch
        {
            // Ignore disconnect failures.
        }
        finally
        {
            _socket?.Dispose();
            _socket = null;
            _receiveCts?.Dispose();
            _receiveCts = null;
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, Action<RemoteSignalMessage> onSignalMessage, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
                {
                    var type = typeElement.GetString();
                    if (!string.IsNullOrWhiteSpace(type))
                    {
                        onSignalMessage(new RemoteSignalMessage(type, json));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            onSignalMessage(new RemoteSignalMessage("socket-closed", "{}"));
        }
    }

    private async Task SendJsonAsync(object payload, CancellationToken cancellationToken)
    {
        if (_socket is null || _socket.State != WebSocketState.Open)
        {
            return;
        }

        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static string BuildWebSocketUrl(string baseUrl, string roomCode, string token, string role)
    {
        var uriBuilder = new UriBuilder(baseUrl.TrimEnd('/'));
        uriBuilder.Path = "/ws";
        uriBuilder.Query = $"room={Uri.EscapeDataString(roomCode)}&role={Uri.EscapeDataString(role)}&token={Uri.EscapeDataString(token)}";
        if (uriBuilder.Scheme == Uri.UriSchemeHttps)
        {
            uriBuilder.Scheme = "wss";
            uriBuilder.Port = -1;
        }
        else if (uriBuilder.Scheme == Uri.UriSchemeHttp)
        {
            uriBuilder.Scheme = "ws";
        }

        return uriBuilder.Uri.ToString();
    }
}

public sealed class RemoteSignalMessage(string type, string rawJson)
{
    public string Type { get; } = type;
    public string RawJson { get; } = rawJson;
}

public sealed class RoomSignalSnapshot
{
    public bool Ok { get; set; }
    public string RoomCode { get; set; } = string.Empty;
    public bool Expired { get; set; }
    public bool PcConnected { get; set; }
    public bool PhoneConnected { get; set; }
    public DateTime? PcLastSeen { get; set; }
    public DateTime? PhoneLastSeen { get; set; }
    public int SignalAgeSeconds { get; set; }
}
