using System.Collections.Concurrent;
using System.Linq;
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

/// <summary>Metered.ca (or any TURN) from env: comma-separated <c>TURN_URLS</c> + <c>TURN_USERNAME</c> / <c>TURN_CREDENTIAL</c>.</summary>
List<object> BuildIceServersFromEnv()
{
    var turnUrlsRaw = Environment.GetEnvironmentVariable("TURN_URLS") ?? "";
    var parts = turnUrlsRaw
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => s.Trim())
        .Where(s => s.Length > 0)
        .ToArray();

    var turnUser = Environment.GetEnvironmentVariable("TURN_USERNAME") ?? "";
    var turnCred = Environment.GetEnvironmentVariable("TURN_CREDENTIAL") ?? "";

    if (parts.Length == 0)
    {
        return [];
    }

    if (parts.Length == 1)
    {
        return [new { urls = parts[0], username = turnUser, credential = turnCred }];
    }

    return [new { urls = parts, username = turnUser, credential = turnCred }];
}

static bool EnvFlagTrue(string? value) =>
    !string.IsNullOrWhiteSpace(value) &&
    (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));

app.MapGet("/ice-config", () =>
{
    // Diagnostic: force Google STUN only (no TURN/Metered) to verify direct host/STUN path vs relay.
    if (EnvFlagTrue(Environment.GetEnvironmentVariable("WEBRTC_STUN_ONLY_TEST")))
    {
        var stunOnly = new object[]
        {
            new { urls = new[] { "stun:stun.l.google.com:19302" } },
        };
        logger.LogWarning(
            "GET /ice-config — WEBRTC_STUN_ONLY_TEST=true: returning Google STUN only (no TURN)");
        return Results.Json(new { iceServers = stunOnly });
    }

    var iceServers = BuildIceServersFromEnv();
    var turnUrlsRaw = Environment.GetEnvironmentVariable("TURN_URLS") ?? "";
    var hasTurnUri =
        turnUrlsRaw.Contains("turn:", StringComparison.OrdinalIgnoreCase) ||
        turnUrlsRaw.Contains("turns:", StringComparison.OrdinalIgnoreCase);
    var hasTurnUser = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TURN_USERNAME"));
    logger.LogInformation(
        "GET /ice-config — entries={Count} envHasTurnUri={TurnUri} envHasTURN_USERNAME={HasUser}",
        iceServers.Count,
        hasTurnUri,
        hasTurnUser);
    return Results.Json(new { iceServers });
});

var rooms = new ConcurrentDictionary<string, RoomState>(StringComparer.OrdinalIgnoreCase);
var socketSendLocks = new ConcurrentDictionary<WebSocket, SemaphoreSlim>();

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

    var html = $$"""
                 <!doctype html>
                 <html>
                 <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta http-equiv="Cache-Control" content="no-store">
                 <style>body{margin:0;background:#0e1218;color:#e7edf7;font-family:Arial}.box{padding:8px}.status{font-size:12px;color:#9fc2e8;white-space:pre-wrap;line-height:1.35}.status-head{font-size:13px;font-weight:700;color:#ffb74d;margin-bottom:4px}.status-head.bad{color:#ff8a80}.meter{font-size:11px;color:#9fe6b1;margin-top:4px}.audio-btn{margin-top:6px;padding:6px 10px;border:1px solid #2b7cff;border-radius:7px;background:#102238;color:#d7e9ff;font-weight:600;display:none;cursor:pointer}.wrap{position:relative;width:100%;height:calc(100vh - 86px)}.wrap video{position:relative;z-index:0;width:100%;height:100%;background:#000;object-fit:contain;display:block}.media-overlay{position:absolute;inset:0;z-index:10;display:none;align-items:center;justify-content:center;background:rgba(0,0,0,.82);color:#e7edf7;font-size:16px;font-weight:700;pointer-events:none;text-align:center;padding:16px;line-height:1.45;white-space:pre-wrap}.media-overlay.show{display:flex}.media-overlay.error{background:rgba(72,16,16,.94);color:#ffc9c9;border:2px solid #c44}</style>
                 </head><body><div class="box status" id="status"><div class="status-head" id="statusHead"></div><span id="statusBody">ICE CONNECTING</span></div><div class="box meter" id="audioMeter">REMOTE MIC PEAK: 0%</div><div class="box"><button id="unmuteAudio" class="audio-btn">UNMUTE AUDIO</button></div><div class="wrap"><video id="remoteVideo" autoplay muted playsinline webkit-playsinline style="width:100%;height:100%;background:#000;object-fit:contain"></video><div id="mediaOverlay" class="media-overlay">Đang chờ hình…</div></div>
                 <script>
                 const room = {{JsonSerializer.Serialize(room)}};
                 const token = {{JsonSerializer.Serialize(token)}};
                 const BUILD_TAG = "pc-preview-join-all-20260508-final";
                 document.body.insertAdjacentHTML("afterbegin",
                   "<div style='padding:4px 8px;color:#00ff99;font-size:12px;font-family:monospace'>BUILD: " + BUILD_TAG + "</div>");
                 console.log("[BUILD]", BUILD_TAG);
                 console.log("[pc-preview] boot query:", window.location.search);
                 console.log("[pc-preview] room=", room, "token=", token, "token.length=", token ? token.length : 0);
                 const iceConfigUrl = location.origin + "/ice-config";
                 const statusHead = document.getElementById("statusHead");
                 const statusBody = document.getElementById("statusBody");
                 const audioMeterEl = document.getElementById("audioMeter");
                 function setPreviewStatus(headline, bodyText, headlineBad) {
                   if (headline) {
                     statusHead.textContent = headline;
                     statusHead.className = "status-head" + (headlineBad ? " bad" : "");
                     statusHead.style.display = "block";
                   } else {
                     statusHead.textContent = "";
                     statusHead.style.display = "none";
                     statusHead.className = "status-head";
                   }
                   statusBody.textContent = bodyText || "";
                 }
                 const unmuteBtn = document.getElementById("unmuteAudio");
                 const remoteVideo = document.getElementById("remoteVideo");
                 try {
                   remoteVideo.setAttribute("playsinline", "");
                   remoteVideo.setAttribute("webkit-playsinline", "");
                   remoteVideo.autoplay = true;
                   remoteVideo.playsInline = true;
                   remoteVideo.muted = true;
                 } catch (ve) { console.warn("[pc-preview] video attrs", ve); }
                 const mediaOverlay = document.getElementById("mediaOverlay");
                 const remoteAudio = document.createElement("audio");
                 remoteAudio.autoplay = true;
                 remoteAudio.playsInline = true;
                 remoteAudio.muted = false;
                 remoteAudio.style.display = "none";
                 document.body.appendChild(remoteAudio);
                 const protocol = location.protocol === "https:" ? "wss" : "ws";
                 const wsUrl = protocol + "://" + location.host + "/ws?room=" + encodeURIComponent(room) + "&role=pc-preview&token=" + encodeURIComponent(token);
                 /** MUST match TitanWebCam/main.js — phone uses these URIs + username/credential from GET /ice-config. Preview must use the same or ICE often stays connecting on PC browsers. */
                 var METERED_ICE_URLS = [
                   "stun:stun.relay.metered.ca:80",
                   "turn:global.relay.metered.ca:80",
                   "turn:global.relay.metered.ca:80?transport=tcp",
                   "turn:global.relay.metered.ca:443",
                   "turns:global.relay.metered.ca:443?transport=tcp"
                 ];
                 /** "relay" = chỉ TURN — hay kẹt ICE trên PC nếu TURN chậm/sai credential. "all" = host + STUN + TURN (khớp hành vi mặc định WebRTC, dễ vào hơn). */
                 var ICE_TRANSPORT_POLICY = "all";
                 /** null = not loaded yet; failed fetch keeps null so ensureIceServers can retry. */
                 var mergedIceServers = null;
                 var iceConfigLoadFailed = false;
                 var hasTurnGlobal = false;
                 var sawRelayCandidate = false;
                 var turnProbeFailed = false;
                 var turnRelayProbeTimer = null;
                 var audioState = "AUDIO WAITING";
                 var audioCtx = null;
                 var audioAnalyser = null;
                 var audioAnalyserData = null;
                 var audioRaf = null;
                 var videoDiagTimer = null;
                 var videoState = "NO VIDEO FRAMES";
                 var lastFrameCount = 0;
                 var fpsEstimate = 0;
                 var videoSizeText = "0x0";

                 function clearTurnRelayProbe() {
                   if (turnRelayProbeTimer) { clearTimeout(turnRelayProbeTimer); turnRelayProbeTimer = null; }
                 }

                 function candidateLooksRelay(c) {
                   if (!c) return false;
                   if (c.type === "relay") return true;
                   var sdp = typeof c.candidate === "string" ? c.candidate : "";
                   return /\btyp\s+relay\b/i.test(sdp);
                 }

                 function remoteCandidateDiag(rc) {
                   if (!rc) return { typ: "?", sdp: "" };
                   var sdp = typeof rc.candidate === "string" ? rc.candidate : "";
                   var typ = rc.type || "?";
                   var m = sdp.match(/\btyp\s+(\w+)/i);
                   if (m) typ = m[1].toLowerCase();
                   return { typ: typ, sdp: sdp };
                 }

                 function logIcePcStates(pc, tag) {
                   if (!pc) return;
                   console.log("[pc-preview][ICE DEBUG]" + (tag ? " " + tag : ""), "iceGatheringState=", pc.iceGatheringState, "iceConnectionState=", pc.iceConnectionState, "connectionState=", pc.connectionState);
                 }

                 function computeHasTurn(list) {
                   for (var i = 0; i < list.length; i++) {
                     var u = list[i].urls;
                     var arr = typeof u === "string" ? [u] : Array.isArray(u) ? u : [];
                     for (var j = 0; j < arr.length; j++) {
                       if (/^turns?:/i.test(String(arr[j]))) return true;
                     }
                   }
                   return false;
                 }

                 async function ensureIceServersForPreview() {
                   if (mergedIceServers !== null && mergedIceServers.length > 0) return;
                   mergedIceServers = null;
                   iceConfigLoadFailed = false;
                   try {
                     var r = await fetch(iceConfigUrl, { cache: "no-store" });
                     if (!r.ok) throw new Error("HTTP " + r.status);
                     var j = await r.json();
                     var base = Array.isArray(j.iceServers) ? j.iceServers : [];
                     if (!base.length) throw new Error("iceServers empty");
                     if (!computeHasTurn(base)) {
                       mergedIceServers = JSON.parse(JSON.stringify(base));
                       console.log("[pc-preview] ICE: using /ice-config as-is (STUN-only / no turn: URLs)");
                     } else {
                       var username = "";
                       var credential = "";
                       for (var si = 0; si < base.length; si++) {
                         var ent = base[si];
                         if (ent && (ent.username || ent.credential)) {
                           username = ent.username || "";
                           credential = ent.credential || "";
                           break;
                         }
                       }
                       mergedIceServers = [{ urls: METERED_ICE_URLS.slice(), username: username, credential: credential }];
                       console.log("[pc-preview] ICE aligned with phone: Metered host list + credentials from /ice-config");
                     }
                   } catch (err) {
                     iceConfigLoadFailed = true;
                     mergedIceServers = null;
                     console.error("[pc-preview] /ice-config fetch failed", err);
                   }
                   hasTurnGlobal = mergedIceServers && mergedIceServers.length > 0 && computeHasTurn(mergedIceServers);
                   console.log("[pc-preview] ICE RTCIceServer entries:", mergedIceServers ? mergedIceServers.length : 0, "; policy=", ICE_TRANSPORT_POLICY, "; has TURN URI:", hasTurnGlobal);
                 }

                 function stopAudioMeter() {
                   if (audioRaf != null) {
                     cancelAnimationFrame(audioRaf);
                     audioRaf = null;
                   }
                   try {
                     if (audioCtx && audioCtx.state !== "closed") audioCtx.close();
                   } catch (err) { console.warn("[pc-preview] close audio ctx", err); }
                   audioCtx = null;
                   audioAnalyser = null;
                   audioAnalyserData = null;
                   audioMeterEl.textContent = "REMOTE MIC PEAK: 0%";
                 }

                 function stopVideoDiagnostics() {
                   if (videoDiagTimer) {
                     clearInterval(videoDiagTimer);
                     videoDiagTimer = null;
                   }
                   videoState = "NO VIDEO FRAMES";
                   fpsEstimate = 0;
                   lastFrameCount = 0;
                   videoSizeText = "0x0";
                 }

                 function sampleVideoStats() {
                   var width = remoteVideo.videoWidth || 0;
                   var height = remoteVideo.videoHeight || 0;
                   videoSizeText = width + "x" + height;
                   var framesNow = 0;
                   var quality = remoteVideo.getVideoPlaybackQuality ? remoteVideo.getVideoPlaybackQuality() : null;
                   if (quality && typeof quality.totalVideoFrames === "number") {
                     framesNow = quality.totalVideoFrames;
                   } else {
                     // Fallback when getVideoPlaybackQuality is unavailable.
                     framesNow = remoteVideo.currentTime > 0 ? Math.round(remoteVideo.currentTime * 15) : 0;
                   }
                   var delta = Math.max(0, framesNow - lastFrameCount);
                   lastFrameCount = framesNow;
                   fpsEstimate = delta;
                   var receiving = (width > 0 && height > 0 && delta > 0);
                   videoState = receiving ? "VIDEO RECEIVING" : "NO VIDEO FRAMES";
                   if (!receiving && remoteVideo.readyState >= 2 && width > 0 && height > 0 && remoteVideo.paused) {
                     videoState = "NO VIDEO FRAMES";
                   }
                 }

                 function startVideoDiagnostics() {
                   stopVideoDiagnostics();
                   sampleVideoStats();
                   videoDiagTimer = setInterval(function () {
                     sampleVideoStats();
                     updateDiag();
                   }, 1000);
                 }

                 function startAudioMeter(stream) {
                   stopAudioMeter();
                   if (!stream) return;
                   try {
                     audioCtx = new AudioContext();
                     var src = audioCtx.createMediaStreamSource(stream);
                     audioAnalyser = audioCtx.createAnalyser();
                     audioAnalyser.fftSize = 512;
                     audioAnalyser.smoothingTimeConstant = 0.72;
                     audioAnalyserData = new Uint8Array(audioAnalyser.fftSize);
                     src.connect(audioAnalyser);
                     function loop() {
                       if (!audioAnalyser || !audioAnalyserData) return;
                       audioAnalyser.getByteTimeDomainData(audioAnalyserData);
                       var sum = 0;
                       for (var i = 0; i < audioAnalyserData.length; i++) {
                         var v = (audioAnalyserData[i] - 128) / 128;
                         sum += v * v;
                       }
                       var rms = Math.sqrt(sum / audioAnalyserData.length);
                       var pct = Math.max(0, Math.min(100, Math.round(rms * 180)));
                       audioMeterEl.textContent = "REMOTE MIC PEAK: " + pct + "%";
                       audioRaf = requestAnimationFrame(loop);
                     }
                     loop();
                   } catch (err) {
                     console.warn("[pc-preview] audio meter failed", err);
                   }
                 }

                 async function playRemoteAudio() {
                   try {
                     await remoteAudio.play();
                     audioState = remoteAudio.muted ? "AUDIO MUTED" : "AUDIO PLAYING";
                     unmuteBtn.style.display = remoteAudio.muted ? "inline-block" : "none";
                     console.log("[pc-preview] AUDIO PLAYING", "readyState=", remoteAudio.readyState, "paused=", remoteAudio.paused, "muted=", remoteAudio.muted);
                   } catch (err) {
                     audioState = "AUDIO BLOCKED";
                     unmuteBtn.style.display = "inline-block";
                     console.error("[pc-preview] AUDIO BLOCKED", err, "readyState=", remoteAudio.readyState, "paused=", remoteAudio.paused, "muted=", remoteAudio.muted);
                   }
                 }

                 unmuteBtn.addEventListener("click", async function () {
                   remoteAudio.muted = false;
                   await playRemoteAudio();
                   updateDiag();
                 });

                 let pc = null;
                 var pendingRemoteCandidates = [];
                 let ws = null;
                 let statsTimer = null;
                 let webrtcStatsMonitorTimer = null;
                 let videoTrackProbe1Timer = null;
                 let videoTrackProbe3Timer = null;
                 var lastStatsFramesDecodedForBlackDetect = -1;
                 var loggedVideoElementNotRendering = false;
                 var didSrcObjectFallback = false;
                 let mediaWaitTimer = null;
                 var gotRemoteOffer = false;
                 var sawVideoTrack = false;
                 var noOfferTimer = null;
                 var wsNotOpenTimer = null;
                 var noOffer5sTimer = null;
                 var videoReceiveDeadlineTimer = null;
                 var previewErrorActive = false;
                 var previewAnswerSentAtMs = null;
                 var loggedWebRtcIceConnectedPreview = false;
                 /** Sau khi gửi answer: ICE không xong hoặc không có khung hình ~11s → báo lỗi (không để treo kết nối im lặng). */
                 var iceHangHardTimer = null;

                 function stopIceHangHardTimer() {
                   if (iceHangHardTimer) { clearTimeout(iceHangHardTimer); iceHangHardTimer = null; }
                 }

                 function startIceHangHardTimer() {
                   stopIceHangHardTimer();
                   iceHangHardTimer = setTimeout(function () {
                     iceHangHardTimer = null;
                     if (previewErrorActive || !pc) return;
                     if (remoteVideo.videoWidth > 0) return;
                     var ice = pc.iceConnectionState;
                     if (ice === "connected" || ice === "completed") {
                       showPreviewError(
                         "KHÔNG NHẬN ĐƯỢC VIDEO\n\nICE đã kết nối nhưng PC vẫn không có khung hình.\nThử đóng/mở Preview hoặc tắt/bật camera trên điện thoại.",
                         "LỖI · ICE OK nhưng không có video");
                       console.error("[pc-preview] ICE_HANG: connected/completed but videoWidth=0 after 11s", { sawVideoTrack: sawVideoTrack });
                       return;
                     }
                     showPreviewError(
                       "KHÔNG NHẬN ĐƯỢC VIDEO\n\nICE kẹt khi tải (trạng thái: " + ice + "). PC không nhận được luồng xem từ điện thoại.\n\nThử: kiểm tra TURN/STUN và env trên server, tắt VPN, đổi mạng, đóng và mở lại Preview.",
                       "LỖI · Không nhận được video — ICE kẹt (" + ice + ")");
                     console.error("[pc-preview] ICE_HANG: ICE not completed, no frames after 11s", { ice: ice, sawVideoTrack: sawVideoTrack, sawRelayCandidate: sawRelayCandidate });
                   }, 11000);
                 }

                 function buildIceHangWarningPrefix() {
                   if (!pc || previewErrorActive || !previewAnswerSentAtMs || remoteVideo.videoWidth > 0) return "";
                   var ice = pc.iceConnectionState;
                   var sec = Math.floor((Date.now() - previewAnswerSentAtMs) / 1000);
                   if (sec < 3) return "";
                   if (ice !== "checking" && ice !== "new" && ice !== "disconnected") return "";
                   return "CHƯA NHẬN ĐƯỢC VIDEO — ICE đang chờ (" + sec + "s). Nếu ~11s vẫn đen → báo lỗi đỏ.\n————————————————————————————\n\n";
                 }

                 function buildIceHeadlineForDiag() {
                   if (!pc || previewErrorActive) return "";
                   var ice = pc.iceConnectionState;
                   if (previewAnswerSentAtMs && remoteVideo.videoWidth === 0 && (ice === "checking" || ice === "new" || ice === "disconnected")) {
                     var sec = Math.floor((Date.now() - previewAnswerSentAtMs) / 1000);
                     if (sec >= 3) return "CHƯA CÓ HÌNH — ICE đang kết nối (" + sec + "s)";
                   }
                   if (ICE_TRANSPORT_POLICY === "relay" && turnProbeFailed && remoteVideo.videoWidth === 0 && (ice === "checking" || ice === "new")) {
                     return "Cảnh báo (relay-only): chưa có relay TURN sau 10s";
                   }
                   return "";
                 }

                 function clearWsNotOpenTimer() {
                   if (wsNotOpenTimer) { clearTimeout(wsNotOpenTimer); wsNotOpenTimer = null; }
                 }

                 function clearNoOffer5sTimer() {
                   if (noOffer5sTimer) { clearTimeout(noOffer5sTimer); noOffer5sTimer = null; }
                 }

                 function clearPreviewWatchdogs() {
                   if (noOfferTimer) { clearTimeout(noOfferTimer); noOfferTimer = null; }
                   clearWsNotOpenTimer();
                   clearNoOffer5sTimer();
                   if (videoReceiveDeadlineTimer) { clearTimeout(videoReceiveDeadlineTimer); videoReceiveDeadlineTimer = null; }
                 }

                 function clearVideoReceiveDeadline() {
                   if (videoReceiveDeadlineTimer) { clearTimeout(videoReceiveDeadlineTimer); videoReceiveDeadlineTimer = null; }
                 }

                 function resetMediaOverlay() {
                   previewErrorActive = false;
                   mediaOverlay.textContent = "Đang chờ hình…";
                   mediaOverlay.classList.remove("show");
                   mediaOverlay.classList.remove("error");
                 }

                 function showPreviewError(overlayMsg, statusLine) {
                   previewErrorActive = true;
                   stopIceHangHardTimer();
                   mediaOverlay.textContent = overlayMsg;
                   mediaOverlay.classList.add("show");
                   mediaOverlay.classList.add("error");
                   if (statusLine) setPreviewStatus(statusLine, "Chi tiết trên khung đỏ và Console (F12).", true);
                 }

                 function startNoOfferWatchdog() {
                   if (noOfferTimer) { clearTimeout(noOfferTimer); noOfferTimer = null; }
                   gotRemoteOffer = false;
                   noOfferTimer = setTimeout(function () {
                     noOfferTimer = null;
                     if (!gotRemoteOffer) {
                       showPreviewError(
                         "LỖI: Không nhận được offer từ điện thoại.\nĐảm bảo điện thoại đã mở camera đúng phòng và đang kết nối.",
                        "LỖI · Hết thời gian chờ SDP offer (30s)");
                      console.error("[pc-preview] TIMEOUT: no WebRTC offer from phone within 30s");
                     }
                   }, 30000);
                 }

                 function startVideoReceiveDeadline() {
                   clearVideoReceiveDeadline();
                   videoReceiveDeadlineTimer = setTimeout(function () {
                     videoReceiveDeadlineTimer = null;
                     if (previewErrorActive) return;
                     if (!pc) return;
                     var w = remoteVideo.videoWidth || 0;
                     if (w > 0) return;
                     var ice = pc.iceConnectionState;
                     var hint = (ice === "connected" || ice === "completed")
                       ? "ICE đã kết nối nhưng không có khung hình — thử đóng/mở cửa sổ preview hoặc kiểm tra điện thoại vẫn đang bật camera."
                       : ("Trạng thái ICE: " + ice + " — kiểm tra TURN, firewall, hoặc thử mạng khác.");
                     showPreviewError(
                       "LỖI: PC không nhận được video từ điện thoại.\n" + hint,
                       "LỖI · Không có video sau 22s (track video: " + (sawVideoTrack ? "có" : "không") + ")");
                     console.error("[pc-preview] TIMEOUT: no video after 22s", { ice: ice, sawVideoTrack: sawVideoTrack, videoWidth: w });
                   }, 22000);
                 }

                 function clearMediaWait() {
                   if (mediaWaitTimer) { clearTimeout(mediaWaitTimer); mediaWaitTimer = null; }
                 }

                 function scheduleMediaWait() {
                   clearMediaWait();
                   mediaOverlay.classList.remove("error");
                   mediaOverlay.classList.remove("show");
                   mediaWaitTimer = setTimeout(function () {
                     mediaWaitTimer = null;
                     if (previewErrorActive) return;
                     if (remoteVideo.videoWidth === 0 || remoteVideo.readyState < 2) {
                       mediaOverlay.textContent = "Đang chờ hình từ điện thoại…\n(Nếu đứng yên quá lâu → sẽ báo lỗi đỏ)";
                       mediaOverlay.classList.remove("error");
                       mediaOverlay.classList.add("show");
                     }
                   }, 4000);
                 }

                 function hideMediaWaitIfPlaying() {
                   if (remoteVideo.videoWidth > 0 && remoteVideo.readyState >= 2) {
                     stopIceHangHardTimer();
                     previewErrorActive = false;
                     mediaOverlay.classList.remove("show");
                     mediaOverlay.classList.remove("error");
                     mediaOverlay.textContent = "Đang chờ hình…";
                     clearVideoReceiveDeadline();
                     updateDiag();
                   }
                 }

                 function stopStats() {
                   if (statsTimer) { clearInterval(statsTimer); statsTimer = null; }
                 }

                 function stopWebRtcStatsMonitor() {
                   if (webrtcStatsMonitorTimer) { clearInterval(webrtcStatsMonitorTimer); webrtcStatsMonitorTimer = null; }
                 }

                 function logReceiversDiagnostics(tagPc) {
                   if (!tagPc || typeof tagPc.getReceivers !== "function") return;
                   try {
                     var rr = tagPc.getReceivers();
                     console.log("[WebRTC] receivers count=", rr.length);
                     rr.forEach(function (rx, idx) {
                       var tk = rx.track;
                       console.log("[WebRTC] receiver[" + idx + "] kind=" + (tk ? tk.kind : "?") + " track.readyState=" + (tk ? tk.readyState : "?") + " track.muted=" + (tk ? tk.muted : "?"));
                     });
                   } catch (rxErr) {
                     console.warn("[WebRTC] logReceiversDiagnostics", rxErr);
                   }
                 }

                 function startWebRtcStatsMonitor() {
                   stopWebRtcStatsMonitor();
                   lastStatsFramesDecodedForBlackDetect = -1;
                   loggedVideoElementNotRendering = false;
                   webrtcStatsMonitorTimer = setInterval(function () {
                     if (!pc || previewErrorActive) return;
                     pc.getStats().then(function (report) {
                       var inbound = null;
                       var codecMime = "?";
                       report.forEach(function (r) {
                         if (r.type !== "inbound-rtp") return;
                         var isVid = r.kind === "video" || r.mediaType === "video";
                         if (isVid) inbound = r;
                       });
                       if (inbound && inbound.codecId) {
                         if (typeof report.get === "function") {
                           var cd = report.get(inbound.codecId);
                           if (cd && cd.mimeType) codecMime = cd.mimeType;
                         }
                         if (codecMime === "?") {
                           report.forEach(function (r) {
                             if (r.type === "codec" && r.id === inbound.codecId && r.mimeType) codecMime = r.mimeType;
                           });
                         }
                       }
                       if (!inbound) {
                         console.log("[WebRTC-STATS] inbound-rtp video (none yet)");
                         return;
                       }
                       var bytesRecv = inbound.bytesReceived != null ? inbound.bytesReceived : "?";
                       var fd = inbound.framesDecoded != null ? inbound.framesDecoded : 0;
                       var fDrop = inbound.framesDropped != null ? inbound.framesDropped : "?";
                       var fw = inbound.frameWidth != null ? inbound.frameWidth : 0;
                       var fh = inbound.frameHeight != null ? inbound.frameHeight : 0;
                       console.log("[WebRTC-STATS]",
                         "bytesReceived=" + bytesRecv,
                         "framesDecoded=" + fd,
                         "framesDropped=" + fDrop,
                         "frameWidth=" + fw,
                         "frameHeight=" + fh,
                         "codec=" + codecMime);

                       var vw = remoteVideo.videoWidth || 0;
                       var vh = remoteVideo.videoHeight || 0;
                       if (lastStatsFramesDecodedForBlackDetect >= 0 && fd > lastStatsFramesDecodedForBlackDetect && vw === 0 && vh === 0) {
                         if (!loggedVideoElementNotRendering) {
                           loggedVideoElementNotRendering = true;
                           console.error("[WebRTC] VIDEO ELEMENT NOT RENDERING");
                         }
                       }
                       lastStatsFramesDecodedForBlackDetect = fd;
                     }).catch(function () {});
                   }, 2000);
                 }

                 function updateDiag() {
                   if (previewErrorActive) return;
                   if (!pc) return;
                   var ice = pc.iceConnectionState;
                   var hangWarn = buildIceHangWarningPrefix();
                   var line = (ice === "checking" || ice === "new") ? "ICE CONNECTING"
                     : ((ice === "connected" || ice === "completed") ? "ICE CONNECTED" : ("ICE: " + ice.toUpperCase()));
                   var failIce = (ice === "failed" || ice === "closed");
                   var turnFailedLine = (turnProbeFailed || failIce) ? "\nTURN FAILED" : "";
                   var cfgLine = iceConfigLoadFailed ? "\nICE CONFIG FAILED (check Render TURN_* env)" : (!hasTurnGlobal ? "\nICE CONFIG EMPTY (set TURN_URLS on Render)" : "");
                   var relayDetect = sawRelayCandidate ? "\nrelay detection: local relay candidate seen" : "\nrelay detection: no local relay yet";
                   var audioLine = "\n" + audioState + "\nremoteAudio.readyState=" + remoteAudio.readyState + " paused=" + remoteAudio.paused + " muted=" + remoteAudio.muted;
                   var videoLine = "\n" + videoState + "\nFPS estimate=" + fpsEstimate + " video=" + videoSizeText;
                   var gatherLine = "\niceGatheringState=" + pc.iceGatheringState + " iceConnectionState=" + pc.iceConnectionState + "\niceTransportPolicy=" + ICE_TRANSPORT_POLICY;
                   pc.getStats().then(function (report) {
                     var best = null;
                     report.forEach(function (r) {
                       if (r.type === "candidate-pair" && r.state === "succeeded") {
                         var p = r.priority || 0;
                         if (!best || p > best.p) best = { p: p, lid: r.localCandidateId, rid: r.remoteCandidateId };
                       }
                     });
                     var extra = "";
                     if (best && best.lid) {
                       var loc = report.get(best.lid);
                       var rem = report.get(best.rid);
                       var lt = loc && loc.candidateType ? loc.candidateType : "?";
                       var rt = rem && rem.candidateType ? rem.candidateType : "?";
                       var relay = lt === "relay" || rt === "relay";
                       console.log("[pc-preview] selected pair:", lt, "→", rt);
                       if (relay) console.log("[pc-preview] relay detection: active path uses TURN relay");
                       extra = (relay ? "\nTURN RELAY ACTIVE" : "") + "\nselected candidate pair type: " + lt + " → " + rt;
                     }
                     var headline = buildIceHeadlineForDiag();
                     var detail = hangWarn + line + extra + cfgLine + relayDetect + turnFailedLine + videoLine + audioLine + gatherLine;
                     setPreviewStatus(headline, detail, false);
                     console.log("[pc-preview][diag]", headline + " || " + detail);
                   }).catch(function () {
                     var headline = buildIceHeadlineForDiag();
                     var detail = hangWarn + line + cfgLine + relayDetect + turnFailedLine + videoLine + audioLine + gatherLine;
                     setPreviewStatus(headline, detail, false);
                   });
                 }

                 function logLocalIce(ev) {
                   if (!ev.candidate) return;
                   var c = ev.candidate;
                   console.log("[pc-preview][ICE FULL CANDIDATE]", c.candidate);
                   console.log("[pc-preview] local candidate type:", c.type || "?", "protocol=", c.protocol || "", "address=", c.address || "");
                 }

                 async function flushPendingRemoteCandidates() {
                   if (!pc || !pc.remoteDescription) return;
                   while (pendingRemoteCandidates.length) {
                     var cand = pendingRemoteCandidates.shift();
                     try {
                       await pc.addIceCandidate(cand);
                       console.log("[WebRTC] REMOTE ICE ADDED");
                       console.log("[pc-preview] REMOTE ICE ADDED (from pending queue)");
                     } catch (err) {
                       console.warn("[pc-preview] pending addIceCandidate failed", err);
                     }
                   }
                 }

                 function closePc() {
                   stopStats();
                   stopWebRtcStatsMonitor();
                   if (videoTrackProbe1Timer) { clearTimeout(videoTrackProbe1Timer); videoTrackProbe1Timer = null; }
                   if (videoTrackProbe3Timer) { clearTimeout(videoTrackProbe3Timer); videoTrackProbe3Timer = null; }
                   lastStatsFramesDecodedForBlackDetect = -1;
                   loggedVideoElementNotRendering = false;
                   didSrcObjectFallback = false;
                   stopIceHangHardTimer();
                   previewAnswerSentAtMs = null;
                   loggedWebRtcIceConnectedPreview = false;
                   pendingRemoteCandidates.length = 0;
                   clearMediaWait();
                   clearPreviewWatchdogs();
                   clearTurnRelayProbe();
                   stopVideoDiagnostics();
                   stopAudioMeter();
                   resetMediaOverlay();
                   remoteAudio.srcObject = null;
                   unmuteBtn.style.display = "none";
                   audioState = "AUDIO WAITING";
                   if (pc) {
                     try { pc.close(); } catch (e) {}
                     pc = null;
                   }
                 }

                 function createPc() {
                   closePc();
                   sawRelayCandidate = false;
                   sawVideoTrack = false;
                   turnProbeFailed = false;
                   clearTurnRelayProbe();
                   var iceServers = JSON.parse(JSON.stringify(mergedIceServers || []));
                   if (!iceServers.length) {
                     setPreviewStatus("LỖI", "ICE CONFIG MISSING — kiểm tra /ice-config hoặc TURN_* trên Render.", true);
                     console.error("[pc-preview] no iceServers — abort RTCPeerConnection");
                     return;
                   }
                   pc = new RTCPeerConnection({ iceServers: iceServers, iceTransportPolicy: "all" });
                   pc.onicecandidateerror = function (e) { console.error("[WebRTC] ICE CANDIDATE ERROR", e); };
                   if (ICE_TRANSPORT_POLICY === "relay") {
                     turnRelayProbeTimer = setTimeout(function () {
                       turnRelayProbeTimer = null;
                       if (!pc) return;
                       if (!sawRelayCandidate) {
                         turnProbeFailed = true;
                         console.log("[pc-preview] TURN FAILED (no relay candidate within 10s)");
                         updateDiag();
                       }
                     }, 10000);
                   }
                   pc.onicecandidate = function (e) {
                     if (e.candidate) {
                       logLocalIce(e);
                       if (candidateLooksRelay(e.candidate)) {
                         sawRelayCandidate = true;
                         turnProbeFailed = false;
                         clearTurnRelayProbe();
                         console.log("[pc-preview] relay detection: local typ relay");
                       }
                       if (ws && ws.readyState === WebSocket.OPEN) {
                         ws.send(JSON.stringify({ type: "ice-candidate", candidate: e.candidate }));
                       }
                     } else {
                       console.log("[pc-preview][ICE FULL CANDIDATE] (end-of-candidates)");
                       logIcePcStates(pc, "after end-of-candidates");
                     }
                     updateDiag();
                   };
                   pc.ontrack = async function (e) {
                     var stream = e.streams && e.streams[0];
                     if (e.track.kind === "audio") {
                       console.log("[pc-preview] AUDIO TRACK RECEIVED");
                       if (stream) {
                         remoteAudio.srcObject = stream;
                         remoteAudio.muted = false;
                         startAudioMeter(stream);
                         await playRemoteAudio();
                         if (remoteAudio.muted) {
                           audioState = "AUDIO MUTED";
                           console.log("[pc-preview] AUDIO MUTED");
                         }
                       }
                       updateDiag();
                       return;
                     }
                     if (e.track.kind === "video" && stream) {
                       sawVideoTrack = true;
                       didSrcObjectFallback = false;
                       if (videoTrackProbe1Timer) { clearTimeout(videoTrackProbe1Timer); videoTrackProbe1Timer = null; }
                       if (videoTrackProbe3Timer) { clearTimeout(videoTrackProbe3Timer); videoTrackProbe3Timer = null; }
                       console.log("[WebRTC] ON TRACK video");
                       console.log("[pc-preview] ON TRACK video", e.track.kind, "streams=", e.streams ? e.streams.length : 0, e.streams);
                       remoteVideo.srcObject = stream;
                       remoteVideo.muted = true;
                       remoteVideo.playsInline = true;
                       remoteVideo.onloadeddata = hideMediaWaitIfPlaying;
                       remoteVideo.onresize = hideMediaWaitIfPlaying;
                       remoteVideo.play().catch(function (playErr) { console.error("[pc-preview] remoteVideo.play() failed", playErr); });
                       videoTrackProbe1Timer = setTimeout(function () {
                         videoTrackProbe1Timer = null;
                         try {
                           console.log("[WebRTC] video element (1s after ON TRACK)",
                             "readyState=" + remoteVideo.readyState,
                             "videoWidth=" + remoteVideo.videoWidth,
                             "videoHeight=" + remoteVideo.videoHeight,
                             "paused=" + remoteVideo.paused,
                             "currentTime=" + remoteVideo.currentTime);
                         } catch (probeErr) { console.warn("[WebRTC] video probe 1s", probeErr); }
                       }, 1000);
                       videoTrackProbe3Timer = setTimeout(function () {
                         videoTrackProbe3Timer = null;
                         if (!pc || previewErrorActive) return;
                         var vw = remoteVideo.videoWidth || 0;
                         var vh = remoteVideo.videoHeight || 0;
                         if (vw > 0 || vh > 0) return;
                         var s = remoteVideo.srcObject;
                         if (!s) return;
                         if (didSrcObjectFallback) return;
                         didSrcObjectFallback = true;
                         console.warn("[WebRTC] fallback: re-assign srcObject + play() after 3s (track ok, element dimensions still 0)");
                         remoteVideo.srcObject = null;
                         remoteVideo.srcObject = s;
                         remoteVideo.play().catch(console.error);
                       }, 3000);
                       scheduleMediaWait();
                       startVideoDiagnostics();
                       updateDiag();
                     }
                   };
                   pc.onicegatheringstatechange = function () {
                     console.log("[WebRTC] ICE GATHER", pc.iceGatheringState);
                     logIcePcStates(pc, "icegatheringstatechange");
                     updateDiag();
                   };
                   pc.oniceconnectionstatechange = function () {
                     console.log("[WebRTC] ICE STATE", pc.iceConnectionState);
                     console.log("[pc-preview] ICE STATE:", pc.iceConnectionState);
                     if ((pc.iceConnectionState === "connected" || pc.iceConnectionState === "completed") && !loggedWebRtcIceConnectedPreview) {
                       loggedWebRtcIceConnectedPreview = true;
                       console.log("[WebRTC] ICE STATE connected");
                     }
                     logIcePcStates(pc, "iceconnectionstatechange");
                     updateDiag();
                     if (pc.iceConnectionState === "connected" || pc.iceConnectionState === "completed") {
                       stopStats();
                       statsTimer = setInterval(function () {
                         pc.getStats().then(function (report) {
                           report.forEach(function (r) {
                             if (r.type === "candidate-pair" && r.state === "succeeded") {
                               var loc = report.get(r.localCandidateId);
                               var rem = report.get(r.remoteCandidateId);
                               if (loc && rem) {
                                 console.log("[pc-preview] selected pair:", loc.candidateType, "→", rem.candidateType);
                                 var relay = loc.candidateType === "relay" || rem.candidateType === "relay";
                                 if (relay) console.log("[pc-preview] relay detection: selected pair uses relay");
                               }
                             }
                           });
                         }).catch(function () {});
                       }, 4000);
                     }
                     if (pc.iceConnectionState === "failed") {
                       stopStats();
                       showPreviewError(
                         "LỖI: Kết nối ICE thất bại.\nKiểm tra TURN, firewall VPN, hoặc thử mạng khác.",
                         "LỖI · ICE failed");
                       console.error("[pc-preview] ICE connection failed");
                     }
                     if (pc.iceConnectionState === "failed" || pc.iceConnectionState === "closed") {
                       stopStats();
                       stopWebRtcStatsMonitor();
                     }
                   };
                   pc.onconnectionstatechange = function () {
                     console.log("[WebRTC] PC STATE", pc.connectionState);
                     logIcePcStates(pc, "connectionstatechange");
                     updateDiag();
                     if (pc.connectionState === "failed") {
                       showPreviewError(
                         "LỖI: Kết nối peer thất bại.\nThử đóng và mở lại cửa sổ preview.",
                         "LỖI · PeerConnection failed");
                       console.error("[pc-preview] RTCPeerConnection.connectionState failed");
                     }
                   };
                 }

                 console.log("[WebRTC] WS URL =", wsUrl);
                 clearWsNotOpenTimer();
                 wsNotOpenTimer = setTimeout(function () {
                   wsNotOpenTimer = null;
                   if (ws && ws.readyState === WebSocket.OPEN) return;
                   setPreviewStatus("WS NOT OPEN", "WebSocket chưa mở sau 3s — kiểm tra URL, token, hoặc server.", true);
                   console.error("[WebRTC] WS not OPEN after 3s readyState=", ws ? ws.readyState : "null");
                 }, 3000);

                 ws = new WebSocket(wsUrl);
                 console.log("[pc-preview] WebSocket connecting… room=", room, "(check token above)");
                 ws.onmessage = async function (evt) {
                   console.log("[WebRTC] WS RAW IN", evt.data);
                   var msg;
                   try {
                     msg = JSON.parse(evt.data);
                   } catch (pe) {
                     console.warn("[WebRTC] WS RAW IN: (invalid JSON)", evt.data);
                     return;
                   }
                   var rawType = msg && msg.type ? msg.type : "?";
                   console.log("[WebRTC] WS RAW IN:", rawType);
                   if (msg.type === "offer" && msg.sdp) {
                     clearNoOffer5sTimer();
                     gotRemoteOffer = true;
                     if (noOfferTimer) { clearTimeout(noOfferTimer); noOfferTimer = null; }
                     try {
                       await ensureIceServersForPreview();
                       console.log("[WebRTC] OFFER RECEIVED");
                       console.log("[pc-preview] OFFER RECEIVED sdp.length=", msg.sdp ? msg.sdp.length : 0);
                       setPreviewStatus("OFFER RECEIVED", "Đã nhận SDP offer — đang xử lý answer…", false);
                       if (pc && pc.remoteDescription) {
                         console.log("[pc-preview] replacing PeerConnection for new offer");
                         closePc();
                       }
                       if (!pc) createPc();
                       if (!pc) {
                         setPreviewStatus("LỖI", "Không tạo được PeerConnection — kiểm tra /ice-config và TURN_*.", true);
                         console.error("[pc-preview] createPc aborted — no ICE servers");
                         return;
                       }
                       setPreviewStatus("", "ICE CONNECTING — đang xử lý offer từ điện thoại…");
                       await pc.setRemoteDescription({ type: "offer", sdp: msg.sdp });
                       console.log("[pc-preview] SET REMOTE DESCRIPTION OK");
                       await flushPendingRemoteCandidates();
                       var answer = await pc.createAnswer();
                       console.log("[pc-preview] ANSWER CREATED");
                       await pc.setLocalDescription(answer);
                       ws.send(JSON.stringify({ type: "answer", sdp: answer.sdp }));
                       console.log("[WebRTC] ANSWER SENT");
                       console.log("[pc-preview] ANSWER SENT");
                       previewAnswerSentAtMs = Date.now();
                       logReceiversDiagnostics(pc);
                       startWebRtcStatsMonitor();
                       startIceHangHardTimer();
                       scheduleMediaWait();
                       startVideoReceiveDeadline();
                       updateDiag();
                       logIcePcStates(pc, "after setLocalDescription answer");
                     } catch (err) {
                       console.error("[pc-preview] offer handling failed", err);
                       closePc();
                       showPreviewError(
                         "LỖI xử lý WebRTC offer/answer:\n" + (err && err.message ? err.message : String(err)),
                         "LỖI · SDP / PeerConnection");
                     }
                     return;
                   }
                   if (msg.type === "ice-candidate" && msg.candidate) {
                     console.log("[pc-preview] REMOTE ICE RECEIVED");
                     if (!pc) {
                       pendingRemoteCandidates.push(msg.candidate);
                       console.log("[pc-preview] REMOTE ICE QUEUED (no PeerConnection yet)");
                       return;
                     }
                     if (!pc.remoteDescription) {
                       pendingRemoteCandidates.push(msg.candidate);
                       console.log("[pc-preview] REMOTE ICE QUEUED (awaiting setRemoteDescription)");
                       return;
                     }
                     try {
                       var rc = msg.candidate;
                       var rd = remoteCandidateDiag(rc);
                       console.log("[pc-preview][ICE FULL REMOTE]", rd.sdp || JSON.stringify(rc));
                       console.log("[pc-preview] remote candidate type:", rd.typ);
                       if (candidateLooksRelay(rc)) console.log("[pc-preview] relay detection: remote typ relay");
                       await pc.addIceCandidate(msg.candidate);
                       console.log("[WebRTC] REMOTE ICE ADDED");
                       console.log("[pc-preview] REMOTE ICE ADDED");
                     } catch (err) { console.warn("[pc-preview] addIceCandidate", err); }
                     updateDiag();
                     return;
                   }
                   if (msg.type === "camera-stopped") {
                     setPreviewStatus("MẤT TÍN HIỆU", "Camera điện thoại dừng hoặc socket đóng.", true);
                     closePc();
                     remoteVideo.srcObject = null;
                     mediaOverlay.classList.remove("show");
                     audioMeterEl.textContent = "REMOTE MIC PEAK: 0%";
                   }
                 };
                 ws.onopen = async function () {
                   clearWsNotOpenTimer();
                   console.log("[WebRTC] WS OPEN pc-preview room=" + room + " tokenLen=" + (token ? token.length : 0));
                   console.log("[pc-preview] WS OPEN pc-preview");
                   setPreviewStatus("", "Đang tải /ice-config…");
                   try {
                     await ensureIceServersForPreview();
                   } catch (e) {
                     console.warn("[pc-preview] ensureIceServersForPreview", e);
                   }
                   if (iceConfigLoadFailed || mergedIceServers === null || !mergedIceServers.length) {
                     setPreviewStatus("CẢNH BÁO ICE", "Không load được iceServers — set TURN_* trên Render hoặc WEBRTC_STUN_ONLY_TEST.", false);
                   } else {
                     var sub = hasTurnGlobal
                       ? "Đã gửi join — chờ SDP offer từ điện thoại."
                       : "STUN-only (không có TURN) — chờ SDP offer từ điện thoại.";
                     setPreviewStatus("Đang chờ điện thoại", sub, false);
                     if (!pc) {
                       createPc();
                       console.log("[pc-preview] RTCPeerConnection created early (same iceServers as /ice-config)");
                     }
                   }
                   ws.send(JSON.stringify({ type: "join", role: "pc-preview", room: room, token: token }));
                   console.log("[WebRTC] JOIN SENT pc-preview", room);
                   ws.send(JSON.stringify({ type: "preview-join", room: room, token: token }));
                   console.log("[WebRTC] PREVIEW JOIN SENT", room);
                   startNoOfferWatchdog();
                   clearNoOffer5sTimer();
                   noOffer5sTimer = setTimeout(function () {
                     noOffer5sTimer = null;
                     if (gotRemoteOffer) return;
                     if (previewErrorActive) return;
                     setPreviewStatus("NO OFFER FROM PHONE", "Đã join nhưng 5s chưa có SDP offer — mở camera trên điện thoại hoặc thử lại.", false);
                     console.warn("[WebRTC] signaling: no offer within 5s after join");
                   }, 5000);
                 };
                 ws.onclose = function () {
                   clearWsNotOpenTimer();
                   clearNoOffer5sTimer();
                   setPreviewStatus("SOCKET ĐÓNG", "Mất kết nối WebSocket — đóng và mở lại cửa sổ Preview.", true);
                   closePc();
                   remoteVideo.srcObject = null;
                   audioMeterEl.textContent = "REMOTE MIC PEAK: 0%";
                 };
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
        logger.LogInformation("WS pc (app) joined room {RoomCode}", roomCode);
    }
    else if (role == "pc-preview")
    {
        room.PcPreviewSocket = socket;
        logger.LogInformation("[WebRTC] pc-preview joined room={Room}", roomCode);
        var flushedPhoneIce = await FlushIceQueueAsync(room.IcePhoneToPreviewQueue, socket, CancellationToken.None);
        if (flushedPhoneIce > 0)
        {
            logger.LogInformation(
                "Room {Room}: flushed {Count} queued ICE phone → pc-preview",
                room.RoomCode,
                flushedPhoneIce);
        }
    }
    else if (role == "phone")
    {
        room.PhoneSocket = socket;
        await SendJsonAsync(room.PcSocket, new { type = "phone-joined" }, CancellationToken.None);
        await SendJsonAsync(room.PhoneSocket, new { type = "signal-online" }, CancellationToken.None);
        logger.LogInformation("[WebRTC] phone joined room={Room}", roomCode);

        // Preview often connects before phone; client-side preview-join then had delivered=false. Push resend now.
        if (room.PcPreviewSocket is { State: WebSocketState.Open })
        {
            await SendJsonAsync(room.PhoneSocket, new { type = "preview-reconnect", room = room.RoomCode }, CancellationToken.None);
            logger.LogInformation(
                "[WebRTC] preview-join notified phone room={Room} delivered=true (pc-preview already connected — resend offer)",
                room.RoomCode);
        }

        var flushedPreviewIce = await FlushIceQueueAsync(room.IcePreviewToPhoneQueue, socket, CancellationToken.None);
        if (flushedPreviewIce > 0)
        {
            logger.LogInformation(
                "Room {Room}: flushed {Count} queued ICE pc-preview → phone",
                room.RoomCode,
                flushedPreviewIce);
        }
    }
    else
    {
        await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "invalid role", CancellationToken.None);
        return;
    }

    var buffer = new byte[16 * 1024];
    while (socket.State == WebSocketState.Open)
    {
        WebSocketReceiveResult result;
        using var messageBuffer = new MemoryStream();
        do
        {
            result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.Count > 0)
            {
                await messageBuffer.WriteAsync(buffer.AsMemory(0, result.Count), CancellationToken.None);
            }
        }
        while (!result.EndOfMessage);

        if (result.MessageType == WebSocketMessageType.Close)
        {
            break;
        }

        if (result.MessageType != WebSocketMessageType.Text)
        {
            break;
        }

        var json = Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Invalid JSON from role={Role} room={Room}; payloadLength={Length}",
                role,
                roomCode,
                json.Length);
            continue;
        }

        using (document)
        {
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

        if (type.Equals("join-room", StringComparison.OrdinalIgnoreCase))
        {
            room.Touch(role);
            await SendJsonAsync(socket, new { type = "joined", ok = true }, CancellationToken.None);
            continue;
        }

        if ((type.Equals("preview-join", StringComparison.OrdinalIgnoreCase) ||
             type.Equals("join", StringComparison.OrdinalIgnoreCase)) &&
            role == "pc-preview")
        {
            room.Touch(role);
            var phoneOpen = room.PhoneSocket is { State: WebSocketState.Open };
            if (!phoneOpen)
            {
                logger.LogWarning(
                    "[WebRTC] preview-join notified phone room={Room} delivered=false reason=phone socket null or closed (phone will get resend when it connects)",
                    room.RoomCode);
            }
            else
            {
                await SendJsonAsync(room.PhoneSocket, new { type = "preview-reconnect", room = room.RoomCode }, CancellationToken.None);
                logger.LogInformation("[WebRTC] preview-join notified phone room={Room} delivered=true", room.RoomCode);
            }

            continue;
        }

        if (type.Equals("audio-level", StringComparison.OrdinalIgnoreCase))
        {
            room.Touch(role);
            if (role == "phone")
            {
                // Keep PC app mic status alive even when preview socket is active.
                await SendRawAsync(room.PcSocket, json, CancellationToken.None);
                await SendRawAsync(room.PcPreviewSocket, json, CancellationToken.None);
                continue;
            }
        }

        if (type.Equals("ice-candidate", StringComparison.OrdinalIgnoreCase) && role == "phone")
        {
            room.Touch(role);
            if (room.PcPreviewSocket is { State: WebSocketState.Open })
            {
                await SendRawAsync(room.PcPreviewSocket, json, CancellationToken.None);
            }
            else if (room.PcSocket is { State: WebSocketState.Open })
            {
                await SendRawAsync(room.PcSocket, json, CancellationToken.None);
            }
            else
            {
                room.IcePhoneToPreviewQueue.Enqueue(json);
                logger.LogInformation(
                    "Room {Room}: queued ICE from phone (preview+pc offline) depth={Depth}",
                    room.RoomCode,
                    room.IcePhoneToPreviewQueue.Count);
            }

            continue;
        }

        if (type.Equals("ice-candidate", StringComparison.OrdinalIgnoreCase) && role == "pc-preview")
        {
            room.Touch(role);
            if (room.PhoneSocket is { State: WebSocketState.Open })
            {
                await SendRawAsync(room.PhoneSocket, json, CancellationToken.None);
            }
            else
            {
                room.IcePreviewToPhoneQueue.Enqueue(json);
                logger.LogInformation(
                    "Room {Room}: queued ICE from pc-preview (phone offline) depth={Depth}",
                    room.RoomCode,
                    room.IcePreviewToPhoneQueue.Count);
            }

            continue;
        }

        WebSocket? target = role switch
        {
            "pc" => room.PhoneSocket,
            "pc-preview" => room.PhoneSocket,
            "phone" => room.PcPreviewSocket is { State: WebSocketState.Open } ? room.PcPreviewSocket : room.PcSocket,
            _ => null,
        };

        if (type.Equals("offer", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("answer", StringComparison.OrdinalIgnoreCase))
        {
            var toPeer = DescribeWsPeer(target, room);
            var ok = target is { State: WebSocketState.Open };
            logger.LogInformation(
                "Signaling room {Room}: SDP {SdpType} len={Len} fromRole={From} → peer={ToPeer} delivered={Ok}",
                room.RoomCode,
                type,
                json.Length,
                role,
                toPeer,
                ok);
            if (!ok)
            {
                string reason;
                if (target is null)
                {
                    var pNull = room.PcPreviewSocket is null || room.PcPreviewSocket.State != WebSocketState.Open;
                    var cNull = room.PcSocket is null || room.PcSocket.State != WebSocketState.Open;
                    reason = pNull && cNull
                        ? "pcPreviewSocket null/closed and pcSocket null/closed"
                        : "selected peer socket not open";
                }
                else
                {
                    reason = "target socket not open";
                }

                logger.LogWarning(
                    "Signaling room {Room}: SDP {SdpType} fromRole={From} not delivered — reason={Reason} ({ToPeer})",
                    room.RoomCode,
                    type,
                    role,
                    reason,
                    toPeer);

                if (type.Equals("offer", StringComparison.OrdinalIgnoreCase) && role == "phone")
                {
                    logger.LogWarning(
                        "[WebRTC] OFFER relay phone→pc-preview room={Room} delivered=false reason={Reason}",
                        room.RoomCode,
                        reason);
                }
            }
            else if (type.Equals("offer", StringComparison.OrdinalIgnoreCase) && role == "phone")
            {
                logger.LogInformation("[WebRTC] OFFER received from phone room={Room}", room.RoomCode);
                var dest = ReferenceEquals(target, room.PcPreviewSocket) ? "pc-preview" : "pc";
                logger.LogInformation(
                    "[WebRTC] OFFER relay phone→{Dest} room={Room} delivered=true",
                    dest,
                    room.RoomCode);
            }
            else if (type.Equals("answer", StringComparison.OrdinalIgnoreCase) && role == "pc-preview")
            {
                logger.LogInformation("[WebRTC] ANSWER relay pc-preview→phone room={Room}", room.RoomCode);
            }
        }

        await SendRawAsync(target, json, CancellationToken.None);
        }
    }

    if (role == "pc")
    {
        room.PcSocket = null;
        logger.LogInformation("PC disconnected from room {RoomCode}", roomCode);
    }
    else if (role == "pc-preview")
    {
        room.PcPreviewSocket = null;
        logger.LogInformation("WS pc-preview left room {RoomCode}", roomCode);
    }
    else
    {
        room.PhoneSocket = null;
        await SendJsonAsync(room.PcSocket, new { type = "phone-left" }, CancellationToken.None);
        logger.LogInformation("WS phone left room {RoomCode}", roomCode);
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

async Task<int> FlushIceQueueAsync(ConcurrentQueue<string> queue, WebSocket? socket, CancellationToken cancellationToken)
{
    if (socket is null || socket.State != WebSocketState.Open)
    {
        return 0;
    }

    var n = 0;
    while (queue.TryDequeue(out var json))
    {
        await SendRawAsync(socket, json, cancellationToken);
        n++;
    }

    return n;
}

string DescribeWsPeer(WebSocket? target, RoomState room)
{
    if (target is null)
    {
        return "none";
    }

    if (ReferenceEquals(target, room.PhoneSocket))
    {
        return "phone";
    }

    if (ReferenceEquals(target, room.PcPreviewSocket))
    {
        return "pc-preview";
    }

    if (ReferenceEquals(target, room.PcSocket))
    {
        return "pc";
    }

    return "unknown";
}

async Task SendJsonAsync(WebSocket? socket, object payload, CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(payload);
    await SendRawAsync(socket, json, cancellationToken);
}

async Task SendRawAsync(WebSocket? socket, string json, CancellationToken cancellationToken)
{
    if (socket is null || socket.State != WebSocketState.Open)
    {
        return;
    }

    var sendGate = socketSendLocks.GetOrAdd(socket, _ => new SemaphoreSlim(1, 1));
    await sendGate.WaitAsync(cancellationToken);
    try
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

    var bytes = Encoding.UTF8.GetBytes(json);
    await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }
    finally
    {
        sendGate.Release();
    }
}

async Task CloseSocketAsync(WebSocket? socket)
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
    finally
    {
        if (socketSendLocks.TryRemove(socket, out var gate))
        {
            gate.Dispose();
        }
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

    /// <summary>Raw JSON signaling messages (type ice-candidate) when pc-preview was offline.</summary>
    public ConcurrentQueue<string> IcePhoneToPreviewQueue { get; } = new();

    /// <summary>Raw JSON when phone was offline.</summary>
    public ConcurrentQueue<string> IcePreviewToPhoneQueue { get; } = new();

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
