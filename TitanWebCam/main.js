(function () {
  const DEFAULT_SIGNALING_SERVER =
    "https://titan-camera-server.onrender.com";
  /** TitanCameraServer: app.Map("/ws", ...) — phone MUST use this path + query params. */
  const SIGNAL_WS_PATH = "/ws";

  /** Metered.ca production ICE URIs (credentials come from signaling GET /ice-config). */
  const METERED_ICE_URLS = [
    "stun:stun.relay.metered.ca:80",
    "turn:global.relay.metered.ca:80",
    "turn:global.relay.metered.ca:80?transport=tcp",
    "turn:global.relay.metered.ca:443",
    "turns:global.relay.metered.ca:443?transport=tcp",
  ];

  const params = new URLSearchParams(window.location.search);
  const room = params.get("room") || "";
  const token = params.get("token") || "";

  const pairHint = document.getElementById("pairHint");
  const roomInput = document.getElementById("room");
  const tokenInput = document.getElementById("token");
  const status = document.getElementById("status");
  const connectButton = document.getElementById("connect");
  const qualitySelect = document.getElementById("quality");
  const startCameraButton = document.getElementById("startCamera");
  const switchCameraButton = document.getElementById("switchCamera");
  const stopCameraButton = document.getElementById("stopCamera");
  const muteMicButton = document.getElementById("muteMic");
  const preview = document.getElementById("preview");
  const iceDiag = document.getElementById("iceDiag");
  const signalBadge = document.getElementById("signalBadge");
  const cameraBadge = document.getElementById("cameraBadge");
  const micBadge = document.getElementById("micBadge");
  const qualityBadge = document.getElementById("qualityBadge");

  roomInput.value = room;
  tokenInput.value = token;

  let ws = null;
  let heartbeatTimer = null;
  let reconnectAttempt = 0;
  let reconnectTimer = null;
  const reconnectDelays = [2000, 5000, 10000, 10000, 10000];
  let mediaStream = null;
  let useBackCamera = true;
  let micEnabled = false;
  let signalState = "DISCONNECTED";
  let peer = null;
  /** Guards stale ws.onclose after user clicks CONNECT again */
  let connectionGeneration = 0;
  let audioLevelTimer = null;
  let micAudioCtx = null;
  let micAnalyser = null;
  let micAnalyserBuffer = null;
  let micRafId = null;
  let lastMicLevelForSignal = 0;
  let micPermissionDenied = false;

  /** ICE: fixed Metered URLs + username/credential from GET {signal}/ice-config. */
  let resolvedIceServers = null;
  let hasTurnConfigured = false;
  let iceConfigFetchFailed = false;

  let peerStatsTimer = null;
  let turnRelayProbeTimer = null;
  /** True after local ICE candidate string contained typ relay (or RTCIceCandidate.type relay). */
  let sawRelayCandidate = false;
  let turnProbeFailed = false;

  function clearTurnRelayProbe() {
    if (turnRelayProbeTimer) {
      clearTimeout(turnRelayProbeTimer);
      turnRelayProbeTimer = null;
    }
  }

  function candidateLooksRelay(c) {
    if (!c) {
      return false;
    }
    if (c.type === "relay") {
      return true;
    }
    const sdp = typeof c.candidate === "string" ? c.candidate : "";
    return /\btyp\s+relay\b/i.test(sdp);
  }

  function logIcePcStates(pc, label) {
    if (!pc) {
      return;
    }
    log(`[ICE DEBUG]${label ? " " + label : ""}`, "iceGatheringState=", pc.iceGatheringState, "| iceConnectionState=", pc.iceConnectionState, "| connectionState=", pc.connectionState);
  }

  function log(...args) {
    console.log("[TitanWebCam]", ...args);
  }

  log("Fixed signaling server:", DEFAULT_SIGNALING_SERVER);
  if (room && token && pairHint) {
    pairHint.style.display = "block";
    roomInput.readOnly = true;
    tokenInput.readOnly = true;
  }

  function setStatus(text) {
    status.textContent = text;
  }

  function setBadge(el, text, mode) {
    el.textContent = text;
    el.className = `badge ${mode}`;
  }

  function refreshOperatorUi() {
    const wsConnected = ws && ws.readyState === WebSocket.OPEN;
    const canStart = wsConnected && signalState === "CONNECTED";
    const cameraOn = !!mediaStream;

    startCameraButton.disabled = !canStart;
    startCameraButton.textContent = !canStart ? "CONNECT FIRST" : (cameraOn ? "CAMERA ON" : "START CAMERA");
    startCameraButton.classList.toggle("pulse", canStart && !cameraOn);
    switchCameraButton.disabled = !cameraOn;
    stopCameraButton.disabled = !cameraOn;
    muteMicButton.textContent = micEnabled ? "MIC ON" : "MIC OFF";

    setBadge(signalBadge, `SIGNAL: ${signalState}`, signalState === "CONNECTED" ? "ok" : signalState === "RECONNECTING" ? "warn" : "bad");
    setBadge(cameraBadge, `CAMERA: ${cameraOn ? "ON" : "OFF"}`, cameraOn ? "ok" : "off");
    setBadge(micBadge, `MIC: ${micEnabled ? "ON" : "OFF"}`, micEnabled ? "ok" : "warn");
    setBadge(qualityBadge, `QUALITY: ${qualitySelect.value.replace("_", " ")}`, "off");

    if (signalState === "CONNECTED") {
      connectButton.textContent = "CONNECTED";
      connectButton.style.background = "#155f45";
    } else if (signalState === "RECONNECTING") {
      connectButton.textContent = "RECONNECTING";
      connectButton.style.background = "#9b7b2f";
    } else {
      connectButton.textContent = "CONNECT TO TITAN PC";
      connectButton.style.background = "#844444";
    }
  }

  function startHeartbeat(roomCode) {
    stopHeartbeat();
    heartbeatTimer = setInterval(() => {
      if (!ws || ws.readyState !== WebSocket.OPEN) {
        return;
      }

      ws.send(JSON.stringify({ type: "heartbeat", role: "phone", room: roomCode }));
    }, 5000);
  }

  function stopHeartbeat() {
    if (heartbeatTimer) {
      clearInterval(heartbeatTimer);
      heartbeatTimer = null;
    }
  }

  function stopAudioLevelReporting() {
    if (audioLevelTimer) {
      clearInterval(audioLevelTimer);
      audioLevelTimer = null;
    }
  }

  function startAudioLevelReporting() {
    stopAudioLevelReporting();
    audioLevelTimer = setInterval(tickSendAudioLevel, 200);
  }

  function tickSendAudioLevel() {
    if (!ws || ws.readyState !== WebSocket.OPEN) {
      return;
    }

    if (micPermissionDenied && micEnabled) {
      ws.send(JSON.stringify({ type: "audio-level", level: 0, mic: false }));
      return;
    }

    const liveMic =
      micEnabled &&
      !!mediaStream &&
      mediaStream.getAudioTracks().some(t => t.readyState === "live");

    if (!liveMic) {
      ws.send(JSON.stringify({ type: "audio-level", level: 0, mic: false }));
      return;
    }

    const level = Math.min(100, Math.max(0, Math.round(lastMicLevelForSignal)));
    ws.send(JSON.stringify({ type: "audio-level", level, mic: true }));
  }

  function stopMicAnalysis() {
    if (micRafId != null) {
      cancelAnimationFrame(micRafId);
      micRafId = null;
    }
    try {
      if (micAudioCtx && micAudioCtx.state !== "closed") {
        micAudioCtx.close();
      }
    } catch (_) {
      /* ignore */
    }
    micAudioCtx = null;
    micAnalyser = null;
    micAnalyserBuffer = null;
    lastMicLevelForSignal = 0;
    updateMicMeterUi(0);
  }

  function updateMicMeterUi(level) {
    lastMicLevelForSignal = level;
    const fill = document.getElementById("micMeterFill");
    const cap = document.getElementById("micMeterCaption");
    if (fill) {
      fill.style.width = `${Math.min(100, Math.max(0, level))}%`;
    }
    if (!cap) {
      return;
    }
    if (micPermissionDenied && micEnabled) {
      cap.textContent = "MIC INPUT · MIC PERMISSION DENIED";
      if (fill) {
        fill.style.width = "0%";
      }
      return;
    }
    if (!micEnabled) {
      cap.textContent = "MIC INPUT · MIC OFF";
      if (fill) {
        fill.style.width = "0%";
      }
      return;
    }
    if (!mediaStream) {
      cap.textContent = "MIC INPUT · MIC ON — START CAMERA";
      if (fill) {
        fill.style.width = "0%";
      }
      return;
    }
    cap.textContent = `MIC INPUT · ${Math.round(level)}%`;
  }

  function startMicAnalysis(stream) {
    stopMicAnalysis();
    if (!micEnabled || !stream) {
      updateMicMeterUi(0);
      return;
    }
    const audioTracks = stream.getAudioTracks().filter(t => t.readyState === "live");
    if (!audioTracks.length) {
      updateMicMeterUi(0);
      return;
    }
    try {
      micAudioCtx = new AudioContext();
      micAnalyser = micAudioCtx.createAnalyser();
      micAnalyser.fftSize = 512;
      micAnalyser.smoothingTimeConstant = 0.65;
      micAnalyserBuffer = new Uint8Array(micAnalyser.fftSize);
      const src = micAudioCtx.createMediaStreamSource(stream);
      src.connect(micAnalyser);

      function loop() {
        if (!micAnalyser || !micAnalyserBuffer) {
          return;
        }
        micAnalyser.getByteTimeDomainData(micAnalyserBuffer);
        let sum = 0;
        for (let i = 0; i < micAnalyserBuffer.length; i++) {
          const v = (micAnalyserBuffer[i] - 128) / 128;
          sum += v * v;
        }
        const rms = Math.sqrt(sum / micAnalyserBuffer.length);
        const pct = Math.min(100, Math.round(rms * 160));
        updateMicMeterUi(pct);
        micRafId = requestAnimationFrame(loop);
      }
      loop();
    } catch (e) {
      log("startMicAnalysis failed", e);
      updateMicMeterUi(0);
    }
  }

  function getVideoConstraints(profile) {
    if (profile === "HD") {
      return { width: { ideal: 1920 }, height: { ideal: 1080 }, frameRate: { ideal: 30, max: 30 }, facingMode: useBackCamera ? "environment" : "user" };
    }

    if (profile === "LOW") {
      return { width: { ideal: 1280 }, height: { ideal: 720 }, frameRate: { ideal: 30, max: 30 }, facingMode: useBackCamera ? "environment" : "user" };
    }

    return { width: { ideal: 1280 }, height: { ideal: 720 }, frameRate: { ideal: 24, max: 24 }, facingMode: useBackCamera ? "environment" : "user" };
  }

  function sendCameraReady(roomCode) {
    if (!ws || ws.readyState !== WebSocket.OPEN) {
      return;
    }

    ws.send(JSON.stringify({
      type: "camera-ready",
      video: true,
      audio: micEnabled,
      quality: qualitySelect.value
    }));
  }

  async function refreshIceServersFromNetwork() {
    const base = DEFAULT_SIGNALING_SERVER.replace(/\/+$/, "");
    try {
      const res = await fetch(`${base}/ice-config`, { cache: "no-store", credentials: "omit" });
      if (!res.ok) {
        throw new Error(`ice-config HTTP ${res.status}`);
      }
      const data = await res.json();
      const list = Array.isArray(data.iceServers) ? data.iceServers : [];
      let username = "";
      let credential = "";
      for (let i = 0; i < list.length; i++) {
        const e = list[i];
        if (e && (e.username || e.credential)) {
          username = e.username || "";
          credential = e.credential || "";
          break;
        }
      }
      resolvedIceServers = [{ urls: METERED_ICE_URLS.slice(), username, credential }];
      hasTurnConfigured = true;
      iceConfigFetchFailed = false;
      log("ICE resolved: Metered URL set + credentials from /ice-config");
      if (!peer && iceDiag) {
        setIceDiag("ICE: —");
      }
    } catch (e) {
      iceConfigFetchFailed = true;
      resolvedIceServers = null;
      hasTurnConfigured = false;
      throw e;
    }
  }

  function getIceServers() {
    if (!resolvedIceServers || resolvedIceServers.length === 0) {
      return [];
    }
    return JSON.parse(JSON.stringify(resolvedIceServers));
  }

  function stopPeerStatsLoop() {
    if (peerStatsTimer) {
      clearInterval(peerStatsTimer);
      peerStatsTimer = null;
    }
  }

  function setIceDiag(text) {
    if (iceDiag) {
      iceDiag.textContent = text;
    }
  }

  async function logSelectedIcePair(pc) {
    if (!pc) {
      return;
    }
    try {
      const report = await pc.getStats();
      let best = null;
      for (const r of report.values()) {
        if (r.type === "candidate-pair" && r.state === "succeeded") {
          const prio = r.priority || 0;
          if (!best || prio > best.prio) {
            best = { prio, localId: r.localCandidateId, remoteId: r.remoteCandidateId };
          }
        }
      }
      if (!best || !best.localId || !best.remoteId) {
        log("Selected ICE pair: (none yet)");
        return;
      }
      const loc = report.get(best.localId);
      const rem = report.get(best.remoteId);
      const lt = loc && loc.candidateType ? loc.candidateType : "?";
      const rt = rem && rem.candidateType ? rem.candidateType : "?";
      log("selected pair:", lt, "→", rt);
      if (lt === "relay" || rt === "relay") {
        log("relay detection: selected pair uses relay");
      }
    } catch (e) {
      log("getStats(selected pair):", e);
    }
  }

  function startPeerStatsLoop(pc) {
    stopPeerStatsLoop();
    peerStatsTimer = setInterval(() => {
      logSelectedIcePair(pc);
    }, 4000);
  }

  function updatePhoneIceDiagnostics(pc) {
    if (!pc) {
      setIceDiag(iceConfigFetchFailed || !hasTurnConfigured ? "ICE CONFIG FAILED\nCheck Render TURN_*" : "ICE: —");
      return;
    }
    const ice = pc.iceConnectionState;
    let headline = "ICE CONNECTING";
    if (ice === "checking" || ice === "new") {
      headline = "ICE CONNECTING";
    } else if (ice === "connected" || ice === "completed") {
      headline = "ICE CONNECTED";
    } else {
      headline = `ICE: ${ice.toUpperCase()}`;
    }
    const failIce = ice === "failed" || ice === "closed";
    const policy = "TRANSPORT: RELAY ONLY";
    const relayDetect = sawRelayCandidate ? "relay detection: local relay candidate seen" : "relay detection: no local relay yet";
    const failLine = turnProbeFailed || failIce ? "\nTURN FAILED" : "";
    const gather = pc.iceGatheringState || "";
    const conn = pc.connectionState || "";
    setIceDiag(`${headline}\n${policy}\n${relayDetect}${failLine}\ngather=${gather} conn=${conn}`);
    pc.getStats().then(report => {
      let best = null;
      for (const r of report.values()) {
        if (r.type === "candidate-pair" && r.state === "succeeded") {
          const prio = r.priority || 0;
          if (!best || prio > best.prio) {
            best = { prio, localId: r.localCandidateId, remoteId: r.remoteCandidateId };
          }
        }
      }
      if (!best || !best.localId) {
        return;
      }
      const loc = report.get(best.localId);
      const rem = report.get(best.remoteId);
      const lt = loc && loc.candidateType ? loc.candidateType : "?";
      const rt = rem && rem.candidateType ? rem.candidateType : "?";
      const relayInUse = lt === "relay" || rt === "relay";
      if (relayInUse) {
        log("relay detection: active path uses TURN relay");
      }
      const relayLine = relayInUse ? "TURN RELAY ACTIVE" : "";
      const pairLine = `PAIR: ${lt} → ${rt}`;
      setIceDiag(
        `${headline}\n${relayLine ? `${relayLine}\n` : ""}${pairLine}\n${policy}\n${relayDetect}${failLine}\ngather=${gather} conn=${conn}`
      );
    }).catch(() => {});
  }

  function attachPeerIceHandlers(pc) {
    pc.oniceconnectionstatechange = () => {
      logIcePcStates(pc, "iceconnectionstatechange");
      updatePhoneIceDiagnostics(pc);
      if (pc.iceConnectionState === "connected" || pc.iceConnectionState === "completed") {
        clearTurnRelayProbe();
        logSelectedIcePair(pc);
        startPeerStatsLoop(pc);
      }
      if (pc.iceConnectionState === "failed") {
        stopPeerStatsLoop();
      }
    };
    pc.onconnectionstatechange = () => {
      logIcePcStates(pc, "connectionstatechange");
      updatePhoneIceDiagnostics(pc);
    };
    pc.onicegatheringstatechange = () => {
      logIcePcStates(pc, "icegatheringstatechange");
      updatePhoneIceDiagnostics(pc);
    };
  }

  async function startWebRtcOffer(roomCode) {
    if (!ws || ws.readyState !== WebSocket.OPEN || !mediaStream) {
      return;
    }

    try {
      await refreshIceServersFromNetwork();
    } catch (e) {
      iceConfigFetchFailed = true;
      log("ICE config failed — cannot start WebRTC", e && e.message ? e.message : e);
      setStatus("ICE CONFIG FAILED\nSet TURN_URLS / TURN_USERNAME / TURN_CREDENTIAL on Render.");
      return;
    }

    const iceServers = getIceServers();
    if (!iceServers.length) {
      iceConfigFetchFailed = true;
      setStatus("ICE CONFIG FAILED\nEmpty /ice-config");
      return;
    }

    clearTurnRelayProbe();
    stopPeerStatsLoop();
    sawRelayCandidate = false;
    turnProbeFailed = false;

    if (peer) {
      try {
        peer.close();
      } catch (_) {}
      peer = null;
    }

    peer = new RTCPeerConnection({
      iceServers,
      iceTransportPolicy: "relay",
    });
    mediaStream.getVideoTracks().forEach(track => peer.addTrack(track, mediaStream));
    if (micEnabled) {
      mediaStream.getAudioTracks().forEach(track => peer.addTrack(track, mediaStream));
    }

    attachPeerIceHandlers(peer);

    peer.onicecandidate = event => {
      if (event.candidate) {
        const c = event.candidate;
        log("[ICE FULL CANDIDATE]", c.candidate);
        if (candidateLooksRelay(c)) {
          sawRelayCandidate = true;
          turnProbeFailed = false;
          clearTurnRelayProbe();
          log("relay detection: local typ relay");
        }
        log("Local ICE candidate type:", c.type || "(unknown)", c.protocol || "", c.address || "");
        if (ws && ws.readyState === WebSocket.OPEN) {
          ws.send(JSON.stringify({ type: "ice-candidate", candidate: event.candidate }));
        }
      } else {
        log("[ICE FULL CANDIDATE] (end-of-candidates)");
        logIcePcStates(peer, "after end-of-candidates");
      }
      updatePhoneIceDiagnostics(peer);
    };

    turnRelayProbeTimer = setTimeout(() => {
      turnRelayProbeTimer = null;
      if (!peer) {
        return;
      }
      if (!sawRelayCandidate) {
        turnProbeFailed = true;
        log("TURN FAILED (no relay candidate within 10s)");
        updatePhoneIceDiagnostics(peer);
      }
    }, 10000);

    const offer = await peer.createOffer();
    await peer.setLocalDescription(offer);
    ws.send(JSON.stringify({ type: "offer", sdp: offer.sdp }));
    updatePhoneIceDiagnostics(peer);
    logIcePcStates(peer, "after setLocalDescription offer");
    setStatus("CAMERA ON\nSENDING WEBRTC OFFER...");
  }

  function sendCameraStopped() {
    if (!ws || ws.readyState !== WebSocket.OPEN) {
      return;
    }

    ws.send(JSON.stringify({
      type: "camera-stopped",
      video: false,
      audio: false
    }));
  }

  async function stopCamera() {
    if (!mediaStream) {
      return;
    }

    stopMicAnalysis();
    mediaStream.getTracks().forEach(track => track.stop());
    mediaStream = null;
    preview.srcObject = null;
    sendCameraStopped();
    clearTurnRelayProbe();
    stopPeerStatsLoop();
    setIceDiag(hasTurnConfigured && !iceConfigFetchFailed ? "ICE: —" : "ICE CONFIG FAILED\nCheck Render TURN_*");
    if (peer) {
      try {
        peer.close();
      } catch (_) {}
      peer = null;
    }
    setStatus("CAMERA OFF");
    refreshOperatorUi();
  }

  async function startCamera() {
    const roomCode = (roomInput.value || "").trim().toUpperCase();
    if (!roomCode) {
      setStatus("Missing room code");
      return;
    }

    if (!ws || ws.readyState !== WebSocket.OPEN || signalState !== "CONNECTED") {
      setStatus("CONNECT FIRST");
      refreshOperatorUi();
      return;
    }

    try {
      if (mediaStream) {
        stopMicAnalysis();
        mediaStream.getTracks().forEach(track => track.stop());
      }

      const constraints = {
        video: getVideoConstraints(qualitySelect.value),
        audio: micEnabled
      };
      mediaStream = await navigator.mediaDevices.getUserMedia(constraints);
      preview.srcObject = mediaStream;
      preview.muted = true;
      await preview.play();
      micPermissionDenied = false;
      startMicAnalysis(mediaStream);
      sendCameraReady(roomCode);
      await startWebRtcOffer(roomCode);
      setStatus(`CAMERA ON\nQUALITY: ${qualitySelect.value}\nMIC: ${micEnabled ? "ON" : "OFF"}`);
    } catch (error) {
      const msg = error && error.message ? error.message : "";
      if (micEnabled && error && (error.name === "NotAllowedError" || /denied/i.test(msg))) {
        micPermissionDenied = true;
      }
      setStatus(`CAMERA ERROR\n${msg || "Permission denied or unsupported."}`);
    }

    refreshOperatorUi();
  }

  function scheduleReconnect(connectFn) {
    if (reconnectAttempt >= reconnectDelays.length) {
      signalState = "DISCONNECTED";
      setStatus("DISCONNECTED\nReconnect failed.");
      log("Reconnect gave up after", reconnectDelays.length, "attempts");
      refreshOperatorUi();
      return;
    }

    const waitMs = reconnectDelays[reconnectAttempt++];
    signalState = "RECONNECTING";
    setStatus(`RECONNECTING\nRetry in ${Math.round(waitMs / 1000)}s...`);
    refreshOperatorUi();
    reconnectTimer = setTimeout(() => connectFn(true), waitMs);
  }

  /**
   * Program.cs: WebSocket at `/ws` with query room, role, token (required before accept).
   * Signaling HTTP base is fixed (no user override).
   */
  function buildPhoneWebSocketUrl(roomCode, tokenValue) {
    const httpsBase = DEFAULT_SIGNALING_SERVER.replace(/\/+$/, "");
    const wsOrigin = httpsBase
      .replace(/^https:\/\//i, "wss://")
      .replace(/^http:\/\//i, "ws://");
    const path = `${SIGNAL_WS_PATH}?room=${encodeURIComponent(roomCode)}&role=${encodeURIComponent("phone")}&token=${encodeURIComponent(tokenValue)}`;
    const url = `${wsOrigin}${path}`;
    log("Phone WebSocket URL:", url);
    return url;
  }

  function closeExistingSocket() {
    if (!ws) {
      return;
    }
    const old = ws;
    ws = null;
    stopHeartbeat();
    stopAudioLevelReporting();
    try {
      old.onopen = null;
      old.onmessage = null;
      old.onerror = null;
      old.onclose = null;
      if (old.readyState === WebSocket.OPEN || old.readyState === WebSocket.CONNECTING) {
        old.close(1000, "replaced");
      }
    } catch (e) {
      log("closeExistingSocket:", e);
    }
  }

  function connect(isRetry) {
    const roomCode = (roomInput.value || "").trim().toUpperCase();
    const pairingToken = (tokenInput.value || "").trim();
    if (!roomCode || !pairingToken) {
      setStatus("Missing room / pairing token\nOpen this page from the Titan AI Live PC QR.");
      signalState = "DISCONNECTED";
      log("CONNECT blocked — missing room or token");
      refreshOperatorUi();
      return;
    }

    if (reconnectTimer) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }

    const gen = ++connectionGeneration;
    closeExistingSocket();

    const wsUrl = buildPhoneWebSocketUrl(roomCode, pairingToken);
    log("Opening WebSocket (gen", gen, ")", wsUrl);

    const socket = new WebSocket(wsUrl);
    ws = socket;

    socket.onopen = async () => {
      if (gen !== connectionGeneration) {
        log("onopen ignored — stale generation", gen, connectionGeneration);
        return;
      }
      const wasReconnect = isRetry || reconnectAttempt > 0;
      reconnectAttempt = 0;
      signalState = "CONNECTED";
      log("WebSocket OPEN — loading ICE config");
      try {
        await refreshIceServersFromNetwork();
      } catch (e) {
        iceConfigFetchFailed = true;
        log("ice-config prefetch error", e);
        if (iceDiag) {
          setIceDiag("ICE CONFIG FAILED\nCheck Render TURN_*");
        }
      }
      log("WebSocket OPEN — sending join-room + hello");

      const joinRoom = {
        type: "join-room",
        room: roomCode,
        token: pairingToken,
        role: "phone",
      };
      socket.send(JSON.stringify(joinRoom));

      socket.send(JSON.stringify({ type: "hello", role: "phone", room: roomCode }));

      startHeartbeat(roomCode);
      startAudioLevelReporting();
      setStatus(
        wasReconnect
          ? "RECONNECTED\nSIGNAL CONNECTED"
          : "CONNECTED\nSIGNAL CONNECTED"
      );
      refreshOperatorUi();
    };

    socket.onmessage = event => {
      log("message:", event.data);
      try {
        const data = JSON.parse(event.data);
        const t = data && data.type;

        if (t === "heartbeat-ack") {
          signalState = "CONNECTED";
          setStatus("HEARTBEAT OK\nSIGNAL CONNECTED");
        } else if (t === "signal-online") {
          signalState = "CONNECTED";
          setStatus("CONNECTED\nSIGNAL CONNECTED");
        } else if (t === "joined" || t === "ok") {
          signalState = "CONNECTED";
          setStatus("CONNECTED\nSIGNAL CONNECTED");
        } else if (t === "pong") {
          signalState = "CONNECTED";
          setStatus("PONG OK\nSIGNAL CONNECTED");
        } else if (t === "room-expired") {
          signalState = "DISCONNECTED";
          setStatus("ROOM EXPIRED");
          stopHeartbeat();
          stopAudioLevelReporting();
          stopCamera();
        } else if (t === "answer" && data.sdp && peer) {
          peer.setRemoteDescription({ type: "answer", sdp: data.sdp }).catch(() => {});
        } else if (t === "ice-candidate" && data.candidate && peer) {
          const rc = data.candidate;
          log("[ICE FULL REMOTE]", rc.candidate != null ? rc.candidate : JSON.stringify(rc));
          const sdp = typeof rc.candidate === "string" ? rc.candidate : "";
          const typMatch = sdp.match(/\btyp\s+(\w+)/i);
          const remoteTyp = typMatch ? typMatch[1].toLowerCase() : rc.type || "?";
          log("remote candidate type:", remoteTyp);
          if (candidateLooksRelay(rc)) {
            log("relay detection: remote typ relay");
          }
          peer.addIceCandidate(data.candidate).catch(() => {});
        } else {
          setStatus(`SIGNAL CONNECTED\n${event.data}`);
        }
      } catch (_) {
        setStatus(`SIGNAL CONNECTED\n${event.data}`);
      }
      refreshOperatorUi();
    };

    socket.onerror = err => {
      log("WebSocket ERROR", err || "(no details — see onclose code)");
      setStatus("SIGNAL ERROR\nSee browser console [TitanWebCam]");
      signalState = "DISCONNECTED";
      refreshOperatorUi();
    };

    socket.onclose = evt => {
      if (gen !== connectionGeneration) {
        log("onclose ignored — stale generation", gen, connectionGeneration);
        return;
      }
      log("WebSocket CLOSED", {
        code: evt.code,
        reason: evt.reason,
        wasClean: evt.wasClean,
      });
      stopHeartbeat();
      stopAudioLevelReporting();
      if (socket === ws) {
        ws = null;
      }
      signalState = "DISCONNECTED";
      setStatus(
        `SIGNAL DISCONNECTED\nclose code ${evt.code}${evt.reason ? `: ${evt.reason}` : ""}`
      );
      refreshOperatorUi();
      scheduleReconnect(connect);
    };
  }

  connectButton.addEventListener("click", () => {
    reconnectAttempt = 0;
    signalState = "RECONNECTING";
    refreshOperatorUi();
    connect(false);
  });

  startCameraButton.addEventListener("click", async () => {
    await startCamera();
  });

  switchCameraButton.addEventListener("click", async () => {
    if (!mediaStream) {
      return;
    }

    useBackCamera = !useBackCamera;
    await startCamera();
  });

  stopCameraButton.addEventListener("click", async () => {
    await stopCamera();
  });

  muteMicButton.addEventListener("click", async () => {
    micPermissionDenied = false;
    micEnabled = !micEnabled;
    if (mediaStream) {
      await startCamera();
    } else {
      updateMicMeterUi(0);
      refreshOperatorUi();
    }
  });

  qualitySelect.addEventListener("change", () => {
    refreshOperatorUi();
  });

  refreshIceServersFromNetwork().catch(e => {
    iceConfigFetchFailed = true;
    log("initial ice-config failed", e && e.message ? e.message : e);
    if (iceDiag) {
      setIceDiag("ICE CONFIG FAILED\nCheck Render TURN_*");
    }
  });

  refreshOperatorUi();
})();
