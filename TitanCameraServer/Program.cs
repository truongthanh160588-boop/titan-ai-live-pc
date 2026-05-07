using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var configuredOrigins = (Environment.GetEnvironmentVariable("CORS_ORIGINS") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowed(origin =>
            {
                if (string.IsNullOrWhiteSpace(origin))
                {
                    return false;
                }

                if (configuredOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
                    origin.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase) ||
                    origin.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                    origin.StartsWith("https://127.0.0.1", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return origin.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase);
            }));
});

var app = builder.Build();
var logger = app.Logger;
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    app.Urls.Add($"http://0.0.0.0:{port}");
}

app.UseCors();
app.UseWebSockets();

var rooms = new ConcurrentDictionary<string, RoomState>(StringComparer.OrdinalIgnoreCase);

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "TitanCameraServer",
}));

app.MapPost("/api/rooms", (CreateRoomRequest request, HttpContext httpContext) =>
{
    if (string.IsNullOrWhiteSpace(request.RoomCode) || string.IsNullOrWhiteSpace(request.Token))
    {
        return Results.BadRequest(new { ok = false, message = "roomCode and token are required" });
    }

    var now = DateTime.UtcNow;
    var state = rooms.AddOrUpdate(
        request.RoomCode.ToUpperInvariant(),
        _ => new RoomState
        {
            RoomCode = request.RoomCode.ToUpperInvariant(),
            Token = request.Token,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(30),
            PcLastSeenUtc = now,
            PhoneLastSeenUtc = null,
        },
        (_, existing) =>
        {
            existing.Token = request.Token;
            existing.CreatedAtUtc = now;
            existing.ExpiresAtUtc = now.AddMinutes(30);
            existing.PcLastSeenUtc = now;
            existing.PhoneLastSeenUtc = null;
            existing.PcSocket = null;
            existing.PhoneSocket = null;
            return existing;
        });

    logger.LogInformation("Room created or refreshed: {RoomCode}", state.RoomCode);
    var origin = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
    var joinUrl = $"{origin}/join?room={state.RoomCode}&token={Uri.EscapeDataString(state.Token)}";
    return Results.Ok(new { ok = true, roomCode = state.RoomCode, joinUrl });
});

app.MapGet("/api/rooms/{roomCode}", (string roomCode) =>
{
    if (!rooms.TryGetValue(roomCode.ToUpperInvariant(), out var state))
    {
        return Results.NotFound(new { ok = false, message = "room not found" });
    }

    var expired = DateTime.UtcNow >= state.ExpiresAtUtc;
    var signalAge = state.PhoneLastSeenUtc.HasValue
        ? (int)Math.Max(0, (DateTime.UtcNow - state.PhoneLastSeenUtc.Value).TotalSeconds)
        : state.PcLastSeenUtc.HasValue
            ? (int)Math.Max(0, (DateTime.UtcNow - state.PcLastSeenUtc.Value).TotalSeconds)
            : int.MaxValue;
    return Results.Ok(new
    {
        ok = true,
        roomCode = state.RoomCode,
        expired,
        pcConnected = state.PcSocket is { State: WebSocketState.Open },
        pcPreviewConnected = state.PcPreviewSocket is { State: WebSocketState.Open },
        phoneConnected = state.PhoneSocket is { State: WebSocketState.Open },
        pcLastSeen = state.PcLastSeenUtc,
        phoneLastSeen = state.PhoneLastSeenUtc,
        signalAgeSeconds = signalAge,
        expiresAtUtc = state.ExpiresAtUtc,
    });
});

app.MapGet("/join", (HttpContext context) =>
{
    var room = context.Request.Query["room"].ToString();
    var token = context.Request.Query["token"].ToString();
    var htmlTemplate = """
                       <!doctype html>
                       <html>
                       <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
                       <title>Titan WebCam</title>
                       <style>
                       body {font-family:Arial,sans-serif;background:#0e1218;color:#e7edf7;padding:24px;}
                       .card {max-width:560px;margin:0 auto;background:#131b25;border:1px solid #2e3f56;border-radius:10px;padding:16px;}
                       button {padding:10px 14px;border:none;border-radius:8px;background:#2b7cff;color:white;font-weight:600;}
                       code {color:#f5c542;}
                       </style></head>
                       <body><div class="card"><h2>Titan WebCam</h2>
                       <p>Room: <code>__ROOM__</code></p>
                       <p>Tap connect to pair with Titan AI Live PC.</p>
                       <button id="connect">CONNECT TO TITAN PC</button>
                       <pre id="status">Ready</pre>
                       </div>
                       <script>
                       const room = __ROOM_JSON__;
                       const token = __TOKEN_JSON__;
                       const protocol = location.protocol === "https:" ? "wss" : "ws";
                       const wsUrl = `${protocol}://${location.host}/ws?room=${encodeURIComponent(room)}&role=phone&token=${encodeURIComponent(token)}`;
                       let ws;
                       let beat;
                       let reconnect = 0;
                       const delays = [2000,5000,10000,10000,10000];
                       function text(v){ document.getElementById("status").textContent = v; }
                       function startBeat(){
                         clearInterval(beat);
                         beat = setInterval(() => {
                           if(ws && ws.readyState === WebSocket.OPEN){
                             ws.send(JSON.stringify({ type: "heartbeat", role: "phone", room }));
                           }
                         }, 5000);
                       }
                       function connect(){
                         ws = new WebSocket(wsUrl);
                         ws.onopen = () => {
                           reconnect = 0;
                           ws.send(JSON.stringify({ type: "hello", role: "phone", room }));
                           startBeat();
                           text("SIGNAL ONLINE");
                         };
                         ws.onmessage = (e) => { if(e.data.includes("room-expired")){ text("ROOM EXPIRED"); return; } text("SIGNAL ONLINE"); };
                         ws.onclose = () => {
                           clearInterval(beat);
                           if(reconnect >= delays.length){ text("DISCONNECTED"); return; }
                           const wait = delays[reconnect++];
                           text(`RECONNECTING ${Math.round(wait/1000)}s...`);
                           setTimeout(connect, wait);
                         };
                       }
                       document.getElementById("connect").addEventListener("click", connect);
                       </script></body></html>
                       """;
    var html = htmlTemplate
        .Replace("__ROOM__", room)
        .Replace("__ROOM_JSON__", JsonSerializer.Serialize(room))
        .Replace("__TOKEN_JSON__", JsonSerializer.Serialize(token));
    return Results.Content(html, "text/html");
});

app.MapGet("/pc-preview", (HttpContext context) =>
{
    var room = context.Request.Query["room"].ToString();
    var token = context.Request.Query["token"].ToString();
    var stun = context.Request.Query["stun"].ToString();
    var turn = context.Request.Query["turn"].ToString();
    var turnUser = context.Request.Query["turnUser"].ToString();
    var turnPass = context.Request.Query["turnPass"].ToString();
    var html = $$"""
                 <!doctype html>
                 <html>
                 <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
                 <style>body{margin:0;background:#0e1218;color:#e7edf7;font-family:Arial}.box{padding:8px}.status{font-size:12px;color:#9fc2e8}video{width:100%;height:calc(100vh - 42px);background:#000;object-fit:contain}</style>
                 </head><body><div class="box status" id="status">CONNECTING...</div><video id="remoteVideo" autoplay playsinline muted></video>
                 <script>
                 const room = {{JsonSerializer.Serialize(room)}};
                 const token = {{JsonSerializer.Serialize(token)}};
                 const stun = {{JsonSerializer.Serialize(stun)}};
                 const turn = {{JsonSerializer.Serialize(turn)}};
                 const turnUser = {{JsonSerializer.Serialize(turnUser)}};
                 const turnPass = {{JsonSerializer.Serialize(turnPass)}};
                 const status = document.getElementById("status");
                 const remoteVideo = document.getElementById("remoteVideo");
                 const protocol = location.protocol === "https:" ? "wss" : "ws";
                 const wsUrl = `${protocol}://${location.host}/ws?room=${encodeURIComponent(room)}&role=pc-preview&token=${encodeURIComponent(token)}`;
                 const iceServers = [];
                 if(stun){ iceServers.push({ urls: stun }); }
                 if(turn){ iceServers.push({ urls: turn, username: turnUser || "", credential: turnPass || "" }); }
                 const pc = new RTCPeerConnection({ iceServers });
                 const ws = new WebSocket(wsUrl);
                 ws.onopen = () => { status.textContent = "CONNECTING..."; ws.send(JSON.stringify({ type: "hello", role: "pc-preview", room })); };
                 ws.onmessage = async (evt) => {
                   const msg = JSON.parse(evt.data);
                   if(msg.type === "offer" && msg.sdp){
                     status.textContent = "RECEIVING VIDEO...";
                     await pc.setRemoteDescription({ type: "offer", sdp: msg.sdp });
                     const answer = await pc.createAnswer();
                     await pc.setLocalDescription(answer);
                     ws.send(JSON.stringify({ type: "answer", sdp: answer.sdp }));
                     return;
                   }
                   if(msg.type === "ice-candidate" && msg.candidate){
                     try { await pc.addIceCandidate(msg.candidate); } catch {}
                     return;
                   }
                   if(msg.type === "camera-stopped"){ status.textContent = "SIGNAL LOST"; }
                 };
                 pc.ontrack = (e) => { remoteVideo.srcObject = e.streams[0]; status.textContent = "VIDEO ACTIVE"; };
                 pc.onicecandidate = (e) => { if(e.candidate){ ws.send(JSON.stringify({ type: "ice-candidate", candidate: e.candidate })); } };
                 ws.onclose = () => { status.textContent = "SIGNAL LOST"; };
                 </script></body></html>
                 """;
    return Results.Content(html, "text/html");
});

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    var roomCode = context.Request.Query["room"].ToString().ToUpperInvariant();
    var role = context.Request.Query["role"].ToString().ToLowerInvariant();
    var token = context.Request.Query["token"].ToString();

    if (!rooms.TryGetValue(roomCode, out var room) || room.Token != token || DateTime.UtcNow >= room.ExpiresAtUtc)
    {
        context.Response.StatusCode = 401;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    room.Touch(role);
    if (role == "pc" && room.PcSocket is { State: WebSocketState.Open })
    {
        logger.LogInformation("PC reconnect for room {RoomCode}", roomCode);
    }

    if (role == "pc")
    {
        room.PcSocket = socket;
    }
    else if (role == "pc-preview")
    {
        room.PcPreviewSocket = socket;
    }
    else if (role == "phone")
    {
        room.PhoneSocket = socket;
        await SendJsonAsync(room.PcSocket, new { type = "phone-joined" }, CancellationToken.None);
        await SendJsonAsync(room.PhoneSocket, new { type = "signal-online" }, CancellationToken.None);
        logger.LogInformation("Phone connected to room {RoomCode}", roomCode);
    }
    else
    {
        await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "invalid role", CancellationToken.None);
        return;
    }

    var buffer = new byte[16 * 1024];
    while (socket.State == WebSocketState.Open)
    {
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            break;
        }

        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        using var document = JsonDocument.Parse(json);
        var type = document.RootElement.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString() ?? string.Empty
            : string.Empty;
        if (type.Equals("heartbeat", StringComparison.OrdinalIgnoreCase))
        {
            room.Touch(role);
            await SendJsonAsync(socket, new { type = "heartbeat-ack" }, CancellationToken.None);
            continue;
        }

        if (type.Equals("ping", StringComparison.OrdinalIgnoreCase))
        {
            room.Touch(role);
            await SendJsonAsync(socket, new { type = "pong" }, CancellationToken.None);
            continue;
        }

        WebSocket? target = role switch
        {
            "pc" => room.PhoneSocket,
            "pc-preview" => room.PhoneSocket,
            "phone" => room.PcPreviewSocket is { State: WebSocketState.Open } ? room.PcPreviewSocket : room.PcSocket,
            _ => null,
        };
        await SendRawAsync(target, json, CancellationToken.None);
    }

    if (role == "pc")
    {
        room.PcSocket = null;
        logger.LogInformation("PC disconnected from room {RoomCode}", roomCode);
    }
    else if (role == "pc-preview")
    {
        room.PcPreviewSocket = null;
    }
    else
    {
        room.PhoneSocket = null;
        await SendJsonAsync(room.PcSocket, new { type = "phone-left" }, CancellationToken.None);
        logger.LogInformation("Phone disconnected from room {RoomCode}", roomCode);
    }
});

_ = Task.Run(async () =>
{
    while (true)
    {
        var now = DateTime.UtcNow;
        foreach (var pair in rooms.ToArray())
        {
            var room = pair.Value;
            if (now >= room.ExpiresAtUtc)
            {
                await SendJsonAsync(room.PcSocket, new { type = "room-expired" }, CancellationToken.None);
                await SendJsonAsync(room.PcPreviewSocket, new { type = "room-expired" }, CancellationToken.None);
                await SendJsonAsync(room.PhoneSocket, new { type = "room-expired" }, CancellationToken.None);
                await CloseSocketAsync(room.PcSocket);
                await CloseSocketAsync(room.PcPreviewSocket);
                await CloseSocketAsync(room.PhoneSocket);
                rooms.TryRemove(pair.Key, out _);
                logger.LogInformation("Room expired and removed: {RoomCode}", room.RoomCode);
                continue;
            }

            if (room.PhoneLastSeenUtc.HasValue &&
                now - room.PhoneLastSeenUtc.Value > TimeSpan.FromSeconds(30) &&
                room.PhoneSocket is { State: WebSocketState.Open })
            {
                await SendJsonAsync(room.PcSocket, new { type = "phone-left" }, CancellationToken.None);
                await CloseSocketAsync(room.PhoneSocket);
                room.PhoneSocket = null;
                logger.LogInformation("Phone heartbeat timeout in room {RoomCode}", room.RoomCode);
            }

            if (room.PcSocket is { State: WebSocketState.Open })
            {
                await SendJsonAsync(room.PcSocket, new { type = "ping" }, CancellationToken.None);
            }

            if (room.PhoneSocket is { State: WebSocketState.Open })
            {
                await SendJsonAsync(room.PhoneSocket, new { type = "ping" }, CancellationToken.None);
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(15));
    }
});

app.Run();

static async Task SendJsonAsync(WebSocket? socket, object payload, CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(payload);
    await SendRawAsync(socket, json, cancellationToken);
}

static async Task SendRawAsync(WebSocket? socket, string json, CancellationToken cancellationToken)
{
    if (socket is null || socket.State != WebSocketState.Open)
    {
        return;
    }

    var bytes = Encoding.UTF8.GetBytes(json);
    await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
}

static async Task CloseSocketAsync(WebSocket? socket)
{
    if (socket is null || socket.State != WebSocketState.Open)
    {
        return;
    }

    try
    {
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "cleanup", CancellationToken.None);
    }
    catch
    {
    }
}

sealed class CreateRoomRequest
{
    public string RoomCode { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}

sealed class RoomState
{
    public string RoomCode { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? PcLastSeenUtc { get; set; }
    public DateTime? PhoneLastSeenUtc { get; set; }
    public WebSocket? PcSocket { get; set; }
    public WebSocket? PcPreviewSocket { get; set; }
    public WebSocket? PhoneSocket { get; set; }

    public void Touch(string role)
    {
        if (role == "pc")
        {
            PcLastSeenUtc = DateTime.UtcNow;
            return;
        }

        if (role == "phone")
        {
            PhoneLastSeenUtc = DateTime.UtcNow;
        }
    }
}
