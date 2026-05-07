(function () {
  const DEFAULT_SIGNALING_BASE = "https://titan-camera-server.onrender.com";
  const params = new URLSearchParams(window.location.search);
  const room = params.get("room") || "";
  const token = params.get("token") || "";
  const queryServer = params.get("server") || "";
  const inferredServer = `${window.location.protocol}//${window.location.host}`;
  const initialServer = queryServer || DEFAULT_SIGNALING_BASE || inferredServer;

  const serverInput = document.getElementById("server");
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
  const signalBadge = document.getElementById("signalBadge");
  const cameraBadge = document.getElementById("cameraBadge");
  const micBadge = document.getElementById("micBadge");
  const qualityBadge = document.getElementById("qualityBadge");

  serverInput.value = initialServer;
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
    muteMicButton.textContent = micEnabled ? "MUTE MIC: OFF" : "MUTE MIC: ON";

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

  function getIceServers() {
    const iceServers = [{ urls: "stun:stun.l.google.com:19302" }];
    const turn = params.get("turn");
    const turnUser = params.get("turnUser");
    const turnPass = params.get("turnPass");
    if (turn) {
      iceServers.push({ urls: turn, username: turnUser || "", credential: turnPass || "" });
    }

    return iceServers;
  }

  async function startWebRtcOffer(roomCode) {
    if (!ws || ws.readyState !== WebSocket.OPEN || !mediaStream) {
      return;
    }

    if (peer) {
      try { peer.close(); } catch {}
      peer = null;
    }

    peer = new RTCPeerConnection({ iceServers: getIceServers() });
    mediaStream.getVideoTracks().forEach(track => peer.addTrack(track, mediaStream));
    peer.onicecandidate = event => {
      if (event.candidate && ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({ type: "ice-candidate", candidate: event.candidate }));
      }
    };
    const offer = await peer.createOffer();
    await peer.setLocalDescription(offer);
    ws.send(JSON.stringify({ type: "offer", sdp: offer.sdp }));
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

    mediaStream.getTracks().forEach(track => track.stop());
    mediaStream = null;
    preview.srcObject = null;
    sendCameraStopped();
    if (peer) {
      try { peer.close(); } catch {}
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
      sendCameraReady(roomCode);
      await startWebRtcOffer(roomCode);
      setStatus(`CAMERA ON\nQUALITY: ${qualitySelect.value}\nMIC: ${micEnabled ? "ON" : "OFF"}`);
    } catch (error) {
      setStatus(`CAMERA ERROR\n${error && error.message ? error.message : "Permission denied or unsupported."}`);
    }

    refreshOperatorUi();
  }

  function scheduleReconnect(connectFn) {
    if (reconnectAttempt >= reconnectDelays.length) {
      signalState = "DISCONNECTED";
      setStatus("DISCONNECTED\nReconnect failed.");
      refreshOperatorUi();
      return;
    }

    const waitMs = reconnectDelays[reconnectAttempt++];
    signalState = "RECONNECTING";
    setStatus(`RECONNECTING\nRetry in ${Math.round(waitMs / 1000)}s...`);
    refreshOperatorUi();
    reconnectTimer = setTimeout(() => connectFn(true), waitMs);
  }

  function connect(isRetry) {
    const server = (serverInput.value || "").trim().replace(/\/$/, "");
    const roomCode = (roomInput.value || "").trim().toUpperCase();
    const pairingToken = (tokenInput.value || "").trim();
    if (!server || !roomCode || !pairingToken) {
      setStatus("Missing server / room / token");
      return;
    }

    if (reconnectTimer) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }

    const wsBase = resolveWsBase(server);
    const wsUrl = `${wsBase}/ws?room=${encodeURIComponent(roomCode)}&role=phone&token=${encodeURIComponent(pairingToken)}`;
    ws = new WebSocket(wsUrl);
    ws.onopen = () => {
      const wasReconnect = isRetry || reconnectAttempt > 0;
      reconnectAttempt = 0;
      signalState = "CONNECTED";
      ws.send(JSON.stringify({ type: "hello", role: "phone", room: roomCode }));
      startHeartbeat(roomCode);
      setStatus(wasReconnect ? "RECONNECTED\nSIGNAL ONLINE" : "CONNECTED\nSIGNAL ONLINE");
      refreshOperatorUi();
    };
    ws.onmessage = event => {
      try {
        const data = JSON.parse(event.data);
        if (data.type === "heartbeat-ack") {
          signalState = "CONNECTED";
          setStatus("HEARTBEAT OK\nSIGNAL ONLINE");
        } else if (data.type === "signal-online") {
          signalState = "CONNECTED";
          setStatus("CONNECTED\nSIGNAL ONLINE");
        } else if (data.type === "room-expired") {
          signalState = "DISCONNECTED";
          setStatus("ROOM EXPIRED");
          stopHeartbeat();
          stopCamera();
        } else if (data.type === "answer" && data.sdp && peer) {
          peer.setRemoteDescription({ type: "answer", sdp: data.sdp }).catch(() => {});
        } else if (data.type === "ice-candidate" && data.candidate && peer) {
          peer.addIceCandidate(data.candidate).catch(() => {});
        } else {
          setStatus(`SIGNAL ONLINE\n${event.data}`);
        }
      } catch {
        setStatus(`SIGNAL ONLINE\n${event.data}`);
      }
      refreshOperatorUi();
    };
    ws.onerror = () => {
      setStatus("SIGNAL WEAK");
      refreshOperatorUi();
    };
    ws.onclose = () => {
      stopHeartbeat();
      signalState = "RECONNECTING";
      refreshOperatorUi();
      scheduleReconnect(connect);
    };
  }

  function resolveWsBase(serverValue) {
    if (serverValue.startsWith("wss://") || serverValue.startsWith("ws://")) {
      return serverValue.replace(/\/ws$/, "").replace(/\/$/, "");
    }

    if (serverValue.startsWith("https://")) {
      return `wss://${serverValue.replace(/^https:\/\//, "").replace(/\/$/, "")}`;
    }

    if (serverValue.startsWith("http://")) {
      return `ws://${serverValue.replace(/^http:\/\//, "").replace(/\/$/, "")}`;
    }

    return `wss://${serverValue.replace(/\/$/, "")}`;
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
    micEnabled = !micEnabled;
    if (mediaStream) {
      await startCamera();
    } else {
      refreshOperatorUi();
    }
  });

  qualitySelect.addEventListener("change", () => {
    refreshOperatorUi();
  });

  refreshOperatorUi();
})();
