using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Linq;
using System.Threading;
using System.IO;
using System.Text.Json;
using TitanAILivePC.Models;
using TitanAILivePC.Core;
using TitanAILivePC.Models.RemoteCamera;
using TitanAILivePC.Services.RemoteCamera;
using TitanAILivePC.Views;

namespace TitanAILivePC.ViewModels;

public sealed class RemoteCameraViewModel : ObservableObject
{
    private readonly RemoteCameraSessionService _sessionService;
    private readonly RemoteCameraSignalingClient _signalingClient;
    private readonly RemoteCameraQrService _qrService;
    private readonly RemoteCameraObsBridge _obsBridge;
    private readonly RemoteCameraWebRtcReceiver _webRtcReceiver;
    private readonly DispatcherTimer _expiryTimer = new();
    private readonly DispatcherTimer _signalTimer = new();
    private readonly RemoteCameraSettings _settings = new();
    private CancellationTokenSource? _sessionCts;
    private RemoteCameraSession? _session;

    private RemoteCameraState _state = RemoteCameraState.Offline;
    private string _roomCode = "-";
    private string _pairingUrl = "-";
    private string _qrPayload = "-";
    private string _deviceName = "-";
    private string _networkQuality = "Unknown";
    private string _streamStats = "-";
    private bool _audioEnabled;
    private static readonly Brush WaitingBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9FE6B1"));
    private static readonly Brush ConnectedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6FD6E8"));
    private static readonly Brush ExpiredBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5A65A"));
    private static readonly Brush DefaultBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D0D7E5"));

    private string _statusMessage = "Remote camera offline.";
    private string _roomExpiryText = "ROOM EXPIRES IN --:--";
    private string _selectedQualityProfile = "LOW";
    private ImageSource? _remoteCameraQrImage;
    private RemoteCameraSignalState _signalState = RemoteCameraSignalState.Offline;
    private string _signalStatusText = "REMOTE SIGNAL: OFFLINE";
    private int _lastSeenSeconds;
    private int _reconnectAttempt;
    private bool _isReconnectRunning;
    private bool _isSignalTestRunning;
    private bool _testRoomCreated;
    private bool _testPhoneConnected;
    private bool _testHeartbeatReceived;
    private bool _testSignalOnline;
    private bool _testReconnectSuccess;
    private bool _testRoomRecoveryOk;
    private DateTime? _lastHeartbeatUtc;
    private DateTime? _lastReconnectUtc;
    private DateTime? _phoneLastSeenUtc;
    private DateTime? _testStartedUtc;
    private string _testDurationText = "00:00:00";
    private string _testResultText = "TEST NOT RUN";
    private readonly DispatcherTimer _testTimer = new();
    private string _lastHeartbeatText = "-";
    private string _lastReconnectText = "-";
    private string _phoneLastSeenText = "-";
    private string _signalLatencyMockText = "N/A";
    private string _qaLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remote_camera_test.log");
    private bool _phoneVideoOn;
    private bool _phoneAudioOn;
    private string _phoneQuality = "N/A";
    private string _videoPreviewState = "CONNECTING...";
    private string _videoPreviewStats = "Resolution: N/A | FPS: N/A";
    private string _remoteWebAppUrl = "https://titan-webcam.vercel.app";
    private string _remoteSignalingServerUrl = "https://camera.titanaudio.vn";
    private string _serverTestStatus = "SERVER UNKNOWN";

    public IReadOnlyList<string> QualityProfiles { get; } = ["LOW", "HD", "SAFE 5G"];

    public string StateText =>
        State switch
        {
            RemoteCameraState.WaitingForPhone => "WAITING FOR PHONE",
            RemoteCameraState.Connected => "CONNECTED",
            RemoteCameraState.Streaming => "STREAMING",
            RemoteCameraState.Pairing => "PAIRING",
            RemoteCameraState.Reconnecting => "RECONNECTING",
            RemoteCameraState.Expired => "EXPIRED",
            RemoteCameraState.Error => "ERROR",
            _ => "OFFLINE",
        };

    public Brush StateBrush =>
        State switch
        {
            RemoteCameraState.Expired => ExpiredBrush,
            RemoteCameraState.WaitingForPhone => WaitingBrush,
            RemoteCameraState.Connected or RemoteCameraState.Streaming => ConnectedBrush,
            _ => DefaultBrush,
        };

    public string SignalStatusText => _signalStatusText;
    public string LastHeartbeatText => _lastHeartbeatText;
    public string LastReconnectText => _lastReconnectText;
    public string PhoneLastSeenText => _phoneLastSeenText;
    public string SignalLatencyMockText => _signalLatencyMockText;
    public string RoomTtlText => RoomExpiryText;
    public string TestDurationText => _testDurationText;
    public string TestResultText => _testResultText;
    public bool TestRoomCreated { get => _testRoomCreated; private set => SetProperty(ref _testRoomCreated, value); }
    public bool TestPhoneConnected { get => _testPhoneConnected; private set => SetProperty(ref _testPhoneConnected, value); }
    public bool TestHeartbeatReceived { get => _testHeartbeatReceived; private set => SetProperty(ref _testHeartbeatReceived, value); }
    public bool TestSignalOnline { get => _testSignalOnline; private set => SetProperty(ref _testSignalOnline, value); }
    public bool TestReconnectSuccess { get => _testReconnectSuccess; private set => SetProperty(ref _testReconnectSuccess, value); }
    public bool TestRoomRecoveryOk { get => _testRoomRecoveryOk; private set => SetProperty(ref _testRoomRecoveryOk, value); }
    public bool PhoneVideoOn { get => _phoneVideoOn; private set => SetProperty(ref _phoneVideoOn, value); }
    public bool PhoneAudioOn { get => _phoneAudioOn; private set => SetProperty(ref _phoneAudioOn, value); }
    public string PhoneQuality { get => _phoneQuality; private set => SetProperty(ref _phoneQuality, value); }
    public string VideoPreviewState { get => _videoPreviewState; private set => SetProperty(ref _videoPreviewState, value); }
    public string VideoPreviewStats { get => _videoPreviewStats; private set => SetProperty(ref _videoPreviewStats, value); }
    public string RemoteWebAppUrl
    {
        get => _remoteWebAppUrl;
        set
        {
            if (!SetProperty(ref _remoteWebAppUrl, value))
            {
                return;
            }

            _settings.WebAppBaseUrl = value.Trim();
        }
    }

    public string RemoteSignalingServerUrl
    {
        get => _remoteSignalingServerUrl;
        set
        {
            if (!SetProperty(ref _remoteSignalingServerUrl, value))
            {
                return;
            }

            _settings.SignalingServerUrl = value.Trim();
            RaisePropertyChanged(nameof(HttpsWarningText));
        }
    }

    public string ServerTestStatus { get => _serverTestStatus; private set => SetProperty(ref _serverTestStatus, value); }
    public string HttpsWarningText =>
        (RemoteWebAppUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         RemoteSignalingServerUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            ? "HTTPS recommended for phone camera."
            : string.Empty;

    public Brush SignalStateBrush =>
        _signalState switch
        {
            RemoteCameraSignalState.Online => WaitingBrush,
            RemoteCameraSignalState.Weak or RemoteCameraSignalState.Reconnecting => ExpiredBrush,
            RemoteCameraSignalState.Error or RemoteCameraSignalState.Disconnected => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E05858")),
            _ => DefaultBrush,
        };

    public bool CanOperateRoom =>
        _session is not null &&
        State != RemoteCameraState.Expired &&
        State != RemoteCameraState.Offline;
    public bool IsRoomExpired => State == RemoteCameraState.Expired;
    public string TurnWarningText =>
        string.IsNullOrWhiteSpace(_settings.TurnServerUrl)
            ? "Remote 4G/5G may require TURN before video streaming."
            : "TURN server configured.";

    public RelayCommand CreateCameraRoomCommand { get; }
    public RelayCommand CopyLinkCommand { get; }
    public RelayCommand ShowQrCommand { get; }
    public RelayCommand DisconnectCameraCommand { get; }
    public RelayCommand AddToObsCommand { get; }
    public RelayCommand ShowCameraInObsCommand { get; }
    public RelayCommand RunSignalTestCommand { get; }
    public RelayCommand SimulateSignalDropCommand { get; }
    public RelayCommand OpenVideoPreviewCommand { get; }
    public RelayCommand SaveConfigCommand { get; }
    public RelayCommand TestServerCommand { get; }

    public RemoteCameraViewModel()
        : this(
            new RemoteCameraSessionService(),
            new RemoteCameraSignalingClient(),
            new RemoteCameraQrService(),
            new RemoteCameraObsBridge(),
            new RemoteCameraWebRtcReceiver())
    {
    }

    public RemoteCameraViewModel(
        RemoteCameraSessionService sessionService,
        RemoteCameraSignalingClient signalingClient,
        RemoteCameraQrService qrService,
        RemoteCameraObsBridge obsBridge,
        RemoteCameraWebRtcReceiver webRtcReceiver)
    {
        _sessionService = sessionService;
        _signalingClient = signalingClient;
        _qrService = qrService;
        _obsBridge = obsBridge;
        _webRtcReceiver = webRtcReceiver;
        _selectedQualityProfile = "LOW";
        _audioEnabled = _settings.EnableRemoteAudio;
        _streamStats = ResolveQualityProfileText(_selectedQualityProfile);

        CreateCameraRoomCommand = new RelayCommand(CreateCameraRoom);
        CopyLinkCommand = new RelayCommand(CopyLink, () => CanOperateRoom);
        ShowQrCommand = new RelayCommand(ShowQr, () => CanOperateRoom);
        DisconnectCameraCommand = new RelayCommand(DisconnectCamera);
        AddToObsCommand = new RelayCommand(async () => await AddToObsAsync(), () => CanOperateRoom);
        ShowCameraInObsCommand = new RelayCommand(async () => await ShowCameraInObsAsync(), () => CanOperateRoom);
        RunSignalTestCommand = new RelayCommand(RunSignalTest);
        SimulateSignalDropCommand = new RelayCommand(async () => await SimulateSignalDropAsync(), () => CanOperateRoom);
        OpenVideoPreviewCommand = new RelayCommand(OpenVideoPreview, () => CanOperateRoom);
        SaveConfigCommand = new RelayCommand(SaveConfig);
        TestServerCommand = new RelayCommand(async () => await TestServerAsync());

        _expiryTimer.Interval = TimeSpan.FromSeconds(1);
        _expiryTimer.Tick += (_, _) => RefreshRoomExpiry();
        _signalTimer.Interval = TimeSpan.FromSeconds(5);
        _signalTimer.Tick += (_, _) => _ = SignalHeartbeatTickAsync();
        _testTimer.Interval = TimeSpan.FromSeconds(1);
        _testTimer.Tick += (_, _) => UpdateTestDuration();
        SetSignalState(RemoteCameraSignalState.Offline, "REMOTE SIGNAL: OFFLINE");
        SyncConfigToSettings();
    }

    public RemoteCameraState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                RaisePropertyChanged(nameof(StateText));
                RaisePropertyChanged(nameof(StateBrush));
                RaisePropertyChanged(nameof(CanOperateRoom));
                RaisePropertyChanged(nameof(IsRoomExpired));
                RaiseCanExecuteForCommands();
            }
        }
    }

    public string RoomCode { get => _roomCode; private set => SetProperty(ref _roomCode, value); }
    public string PairingUrl { get => _pairingUrl; private set => SetProperty(ref _pairingUrl, value); }
    public string QrPayload { get => _qrPayload; private set => SetProperty(ref _qrPayload, value); }
    public string DeviceName { get => _deviceName; private set => SetProperty(ref _deviceName, value); }
    public string NetworkQuality { get => _networkQuality; private set => SetProperty(ref _networkQuality, value); }
    public string StreamStats { get => _streamStats; private set => SetProperty(ref _streamStats, value); }
    public bool AudioEnabled
    {
        get => _audioEnabled;
        set
        {
            if (!SetProperty(ref _audioEnabled, value))
            {
                return;
            }

            _settings.EnableRemoteAudio = value;
        }
    }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string RoomExpiryText { get => _roomExpiryText; private set => SetProperty(ref _roomExpiryText, value); }
    public ImageSource? RemoteCameraQrImage { get => _remoteCameraQrImage; private set => SetProperty(ref _remoteCameraQrImage, value); }
    public string SelectedQualityProfile
    {
        get => _selectedQualityProfile;
        set
        {
            if (!SetProperty(ref _selectedQualityProfile, value))
            {
                return;
            }

            StreamStats = ResolveQualityProfileText(value);
            _settings.PreferredBitrateKbps = ParseBitrateFromProfile(value);
            _settings.PreferredFps = ParseFpsFromProfile(value);
            _settings.PreferredResolution = ParseResolutionFromProfile(value);
        }
    }

    private void CreateCameraRoom()
    {
        SyncConfigToSettings();
        _session = _sessionService.CreateSession(_settings);
        _session.QrPayload = _qrService.BuildQrPayload(_session);
        _sessionCts?.Cancel();
        _sessionCts = new CancellationTokenSource();
        _ = InitializeServerRoomAsync(_session, _sessionCts.Token);

        Log($"[REMOTE CAMERA] Room created: {_session.RoomCode}");
        WriteQaLog($"room created: {_session.RoomCode}");
        State = _session.State;
        RoomCode = _session.RoomCode;
        PairingUrl = _session.PairingUrl;
        QrPayload = _session.QrPayload;
        RemoteCameraQrImage = _qrService.CreateQrImageSource(PairingUrl);
        Log($"[REMOTE CAMERA] QR generated for room {_session.RoomCode}");
        DeviceName = "Waiting for Titan WebCam...";
        NetworkQuality = "Not connected";
        StreamStats = ResolveQualityProfileText(SelectedQualityProfile);
        AudioEnabled = false;
        StatusMessage = "Room created. Waiting for phone to pair.";
        TestRoomCreated = true;
        RefreshRoomExpiry();
        _expiryTimer.Start();
        _signalTimer.Start();
        RaiseCanExecuteForCommands();
        VideoPreviewState = "CONNECTING...";
        VideoPreviewStats = "Resolution: N/A | FPS: N/A";
    }

    private void CopyLink()
    {
        if (!CanOperateRoom || string.IsNullOrWhiteSpace(PairingUrl) || PairingUrl == "-")
        {
            MessageBox.Show("Hay tao room truoc khi copy link.", "Remote Camera", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText(PairingUrl);
        StatusMessage = "Pairing URL copied.";
    }

    private void ShowQr()
    {
        if (!CanOperateRoom || string.IsNullOrWhiteSpace(QrPayload) || QrPayload == "-")
        {
            MessageBox.Show("Hay tao room truoc khi hien thi QR.", "Remote Camera", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        var qrWindow = new RemoteCameraQrWindow(RoomCode, PairingUrl, RemoteCameraQrImage)
        {
            Owner = owner,
        };
        _ = qrWindow.ShowDialog();
    }

    private void DisconnectCamera()
    {
        if (_session is null)
        {
            return;
        }

        _sessionService.Disconnect(_session);
        _sessionCts?.Cancel();
        _signalTimer.Stop();
        _testTimer.Stop();
        _ = _signalingClient.DisconnectAsync();
        _session = null;
        _expiryTimer.Stop();
        State = RemoteCameraState.Offline;
        DeviceName = "-";
        NetworkQuality = "Unknown";
        RoomExpiryText = "ROOM EXPIRES IN --:--";
        QrPayload = "-";
        RemoteCameraQrImage = null;
        StatusMessage = "Remote camera disconnected.";
        SetSignalState(RemoteCameraSignalState.Offline, "REMOTE SIGNAL: OFFLINE");
        VideoPreviewState = "SIGNAL LOST";
        RaiseCanExecuteForCommands();
    }

    private async Task AddToObsAsync()
    {
        if (!CanOperateRoom)
        {
            return;
        }

        var result = await _obsBridge.AddToObsAsync();
        StatusMessage = result;
    }

    private async Task ShowCameraInObsAsync()
    {
        if (!CanOperateRoom)
        {
            return;
        }

        var result = await _obsBridge.ShowInObsAsync();
        StatusMessage = result;
    }

    private void RefreshRoomExpiry()
    {
        if (_session is null)
        {
            RoomExpiryText = "ROOM EXPIRES IN --:--";
            return;
        }

        var remaining = _session.RemainingSeconds;
        if (_session.IsExpired)
        {
            _expiryTimer.Stop();
            _signalTimer.Stop();
            _testTimer.Stop();
            _sessionCts?.Cancel();
            _ = _signalingClient.DisconnectAsync();
            State = RemoteCameraState.Expired;
            RoomExpiryText = "ROOM EXPIRES IN 00:00";
            StatusMessage = "Room expired. Create a new camera room.";
            RemoteCameraQrImage = null;
            Log("[REMOTE CAMERA] Room expired");
            SetSignalState(RemoteCameraSignalState.Disconnected, "REMOTE SIGNAL: LOST");
            VideoPreviewState = "SIGNAL LOST";
            WriteQaLog("room expired");
            RaiseCanExecuteForCommands();
            return;
        }

        var minutes = remaining / 60;
        var seconds = remaining % 60;
        RoomExpiryText = $"ROOM EXPIRES IN {minutes:00}:{seconds:00}";
    }

    private static string ResolveQualityProfileText(string profile) =>
        profile switch
        {
            "HD" => "1920x1080 / 30fps / 4500 kbps",
            "SAFE 5G" => "1280x720 / 24fps / 1800 kbps",
            _ => "1280x720 / 30fps / 2500 kbps",
        };

    private static int ParseBitrateFromProfile(string profile) => profile switch
    {
        "HD" => 4500,
        "SAFE 5G" => 1800,
        _ => 2500,
    };

    private static int ParseFpsFromProfile(string profile) => profile switch
    {
        "SAFE 5G" => 24,
        _ => 30,
    };

    private static string ParseResolutionFromProfile(string profile) => profile switch
    {
        "HD" => "1920x1080",
        _ => "1280x720",
    };

    private void RaiseCanExecuteForCommands()
    {
        CopyLinkCommand.RaiseCanExecuteChanged();
        ShowQrCommand.RaiseCanExecuteChanged();
        AddToObsCommand.RaiseCanExecuteChanged();
        ShowCameraInObsCommand.RaiseCanExecuteChanged();
        SimulateSignalDropCommand.RaiseCanExecuteChanged();
        OpenVideoPreviewCommand.RaiseCanExecuteChanged();
    }

    private static void Log(string message) => System.Diagnostics.Debug.WriteLine(message);

    private async Task InitializeServerRoomAsync(RemoteCameraSession session, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _signalingClient.CreateRoomAsync(session, _settings, cancellationToken);
            if (!created)
            {
                StatusMessage = "Signaling server room create failed.";
                return;
            }

            PairingUrl = session.PairingUrl;
            QrPayload = _qrService.BuildQrPayload(session);
            RemoteCameraQrImage = _qrService.CreateQrImageSource(PairingUrl);
            await _signalingClient.ConnectPcAsync(session, _settings, HandleSignalMessage, cancellationToken);
            State = RemoteCameraState.WaitingForPhone;
            StatusMessage = "Room is live on signaling server. Waiting for phone.";
            SetSignalState(RemoteCameraSignalState.Waiting, "REMOTE SIGNAL: WAITING");
        }
        catch (Exception ex)
        {
            State = RemoteCameraState.Error;
            StatusMessage = $"Signaling error: {ex.Message}";
            SetSignalState(RemoteCameraSignalState.Error, "REMOTE SIGNAL: ERROR");
        }
    }

    private async Task SignalHeartbeatTickAsync()
    {
        if (_session is null || State is RemoteCameraState.Expired or RemoteCameraState.Offline)
        {
            return;
        }

        try
        {
            await _signalingClient.SendHeartbeatAsync(_sessionCts?.Token ?? CancellationToken.None);
            var snapshot = await _signalingClient.GetRoomSnapshotAsync(_session.RoomCode, _settings, _sessionCts?.Token ?? CancellationToken.None);
            if (snapshot is null)
            {
                await BeginReconnectAsync();
                return;
            }

            if (snapshot.Expired)
            {
                State = RemoteCameraState.Expired;
                SetSignalState(RemoteCameraSignalState.Disconnected, "REMOTE SIGNAL: LOST");
                StatusMessage = "Room expired by signaling server.";
                return;
            }

            _lastSeenSeconds = snapshot.SignalAgeSeconds;
            _lastHeartbeatUtc = snapshot.PcLastSeen ?? DateTime.UtcNow;
            _phoneLastSeenUtc = snapshot.PhoneLastSeen;
            _lastHeartbeatText = _lastHeartbeatUtc.HasValue ? _lastHeartbeatUtc.Value.ToLocalTime().ToString("HH:mm:ss") : "-";
            _phoneLastSeenText = _phoneLastSeenUtc.HasValue ? $"{(int)Math.Max(0, (DateTime.UtcNow - _phoneLastSeenUtc.Value).TotalSeconds)}s ago" : "Not connected";
            _signalLatencyMockText = $"{snapshot.SignalAgeSeconds * 10} ms (mock)";
            RaisePropertyChanged(nameof(LastHeartbeatText));
            RaisePropertyChanged(nameof(PhoneLastSeenText));
            RaisePropertyChanged(nameof(SignalLatencyMockText));
            NetworkQuality = $"Remote Internet | Last seen: {_lastSeenSeconds}s ago | Transport: WebSocket signaling";
            TestHeartbeatReceived = true;
            if (!snapshot.PhoneConnected)
            {
                if (State != RemoteCameraState.WaitingForPhone)
                {
                    State = RemoteCameraState.WaitingForPhone;
                    DeviceName = "Waiting for Titan WebCam...";
                    Log("[REMOTE CAMERA] Phone left");
                    WriteQaLog("phone left");
                }

                SetSignalState(RemoteCameraSignalState.Waiting, "REMOTE SIGNAL: WAITING");
                return;
            }
            TestPhoneConnected = true;

            if (snapshot.SignalAgeSeconds >= 30)
            {
                SetSignalState(RemoteCameraSignalState.Reconnecting, "REMOTE SIGNAL: RECONNECTING");
                State = RemoteCameraState.Reconnecting;
                Log("[REMOTE CAMERA] Reconnecting");
                await BeginReconnectAsync();
                return;
            }

            if (snapshot.SignalAgeSeconds >= 15)
            {
                if (SetSignalState(RemoteCameraSignalState.Weak, "REMOTE SIGNAL: WEAK"))
                {
                    Log("[REMOTE CAMERA] Signal weak");
                }
                return;
            }

            if (State != RemoteCameraState.Connected)
            {
                State = RemoteCameraState.Connected;
                DeviceName = "Titan WebCam Phone";
                StatusMessage = "Phone connected via signaling server.";
            }

            if (SetSignalState(RemoteCameraSignalState.Online, "REMOTE SIGNAL: ONLINE"))
            {
                Log("[REMOTE CAMERA] Signal online");
                WriteQaLog("signal online");
            }
            TestSignalOnline = true;
            if (TestReconnectSuccess && !_session.IsExpired)
            {
                TestRoomRecoveryOk = true;
                _testResultText = "SIGNAL STABILITY PASS";
                RaisePropertyChanged(nameof(TestResultText));
            }
        }
        catch
        {
            await BeginReconnectAsync();
        }
    }

    private void HandleSignalMessage(RemoteSignalMessage signalMessage)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(() =>
        {
            var type = signalMessage.Type;
            if (type.Equals("phone-joined", StringComparison.OrdinalIgnoreCase))
            {
                State = RemoteCameraState.Pairing;
                StatusMessage = "Phone joined room. Pairing...";
                SetSignalState(RemoteCameraSignalState.Online, "REMOTE SIGNAL: ONLINE");
                return;
            }

            if (type.Equals("camera-ready", StringComparison.OrdinalIgnoreCase))
            {
                ApplyCameraReadySignal(signalMessage.RawJson);
                return;
            }

            if (type.Equals("camera-stopped", StringComparison.OrdinalIgnoreCase))
            {
                ApplyCameraStoppedSignal();
                return;
            }

            if (type.Equals("phone-left", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("leave", StringComparison.OrdinalIgnoreCase))
            {
                State = RemoteCameraState.WaitingForPhone;
                DeviceName = "Waiting for Titan WebCam...";
                NetworkQuality = "Not connected";
                StatusMessage = "Phone disconnected.";
                SetSignalState(RemoteCameraSignalState.Waiting, "REMOTE SIGNAL: WAITING");
                VideoPreviewState = "CONNECTING...";
                Log("[REMOTE CAMERA] Phone left");
                WriteQaLog("phone left");
                return;
            }

            if (type.Equals("room-expired", StringComparison.OrdinalIgnoreCase))
            {
                State = RemoteCameraState.Expired;
                StatusMessage = "Room expired by server.";
                SetSignalState(RemoteCameraSignalState.Disconnected, "REMOTE SIGNAL: LOST");
                VideoPreviewState = "SIGNAL LOST";
                return;
            }

            if (type.Equals("socket-closed", StringComparison.OrdinalIgnoreCase))
            {
                _ = BeginReconnectAsync();
            }
        }, DispatcherPriority.Background);
    }

    private async Task BeginReconnectAsync()
    {
        if (_session is null || _session.IsExpired || _isReconnectRunning)
        {
            return;
        }

        _isReconnectRunning = true;
        try
        {
            SetSignalState(RemoteCameraSignalState.Reconnecting, "REMOTE SIGNAL: RECONNECTING");
            State = RemoteCameraState.Reconnecting;
            var delays = new[] { 2, 5, 10, 10, 10 };
            WriteQaLog("reconnect begin");
            for (var i = 0; i < delays.Length; i++)
            {
                _reconnectAttempt = i + 1;
                await Task.Delay(TimeSpan.FromSeconds(delays[i]));
                try
                {
                    await _signalingClient.ConnectPcAsync(_session, _settings, HandleSignalMessage, _sessionCts?.Token ?? CancellationToken.None);
                    SetSignalState(RemoteCameraSignalState.Online, "REMOTE SIGNAL: ONLINE");
                    State = RemoteCameraState.Connected;
                    StatusMessage = "Signal reconnected.";
                    _lastReconnectUtc = DateTime.UtcNow;
                    _lastReconnectText = _lastReconnectUtc.Value.ToLocalTime().ToString("HH:mm:ss");
                    RaisePropertyChanged(nameof(LastReconnectText));
                    TestReconnectSuccess = true;
                    WriteQaLog("reconnect success");
                    _reconnectAttempt = 0;
                    return;
                }
                catch
                {
                    // keep retrying
                }
            }

            SetSignalState(RemoteCameraSignalState.Disconnected, "REMOTE SIGNAL: LOST");
            StatusMessage = "Signal lost. Reconnect failed.";
            Log("[REMOTE CAMERA] Signal lost");
            VideoPreviewState = "SIGNAL LOST";
            _testResultText = "SIGNAL RECOVERY FAILED";
            RaisePropertyChanged(nameof(TestResultText));
            WriteQaLog("reconnect failed");
        }
        finally
        {
            _isReconnectRunning = false;
        }
    }

    private bool SetSignalState(RemoteCameraSignalState signalState, string text)
    {
        var changed = _signalState != signalState || !string.Equals(_signalStatusText, text, StringComparison.Ordinal);
        _signalState = signalState;
        _signalStatusText = text;
        RaisePropertyChanged(nameof(SignalStatusText));
        RaisePropertyChanged(nameof(SignalStateBrush));
        return changed;
    }

    private void RunSignalTest()
    {
        _isSignalTestRunning = true;
        _testStartedUtc = DateTime.UtcNow;
        _testDurationText = "00:00:00";
        RaisePropertyChanged(nameof(TestDurationText));
        TestRoomCreated = _session is not null;
        TestPhoneConnected = false;
        TestHeartbeatReceived = false;
        TestSignalOnline = false;
        TestReconnectSuccess = false;
        TestRoomRecoveryOk = false;
        _testResultText = "TEST RUNNING";
        RaisePropertyChanged(nameof(TestResultText));
        _testTimer.Start();
    }

    private async Task SimulateSignalDropAsync()
    {
        if (_session is null)
        {
            return;
        }

        WriteQaLog("simulate signal drop");
        await _signalingClient.DisconnectAsync();
        await BeginReconnectAsync();
    }

    private void UpdateTestDuration()
    {
        if (!_isSignalTestRunning || !_testStartedUtc.HasValue)
        {
            return;
        }

        var elapsed = DateTime.UtcNow - _testStartedUtc.Value;
        _testDurationText = elapsed.ToString(@"hh\:mm\:ss");
        RaisePropertyChanged(nameof(TestDurationText));
    }

    private void WriteQaLog(string eventText)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{RoomCode}] {eventText}";
            File.AppendAllLines(_qaLogPath, [line]);
        }
        catch
        {
            // Ignore QA log failures.
        }
    }

    private void ApplyCameraReadySignal(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            PhoneVideoOn = root.TryGetProperty("video", out var videoProp) && videoProp.ValueKind == JsonValueKind.True;
            PhoneAudioOn = root.TryGetProperty("audio", out var audioProp) && audioProp.ValueKind == JsonValueKind.True;
            PhoneQuality = root.TryGetProperty("quality", out var qualityProp) && qualityProp.ValueKind == JsonValueKind.String
                ? (qualityProp.GetString() ?? "N/A")
                : "N/A";
        }
        catch
        {
            PhoneVideoOn = true;
            PhoneAudioOn = false;
            PhoneQuality = "UNKNOWN";
        }

        State = RemoteCameraState.Connected;
        DeviceName = "PHONE CAMERA READY";
        StreamStats = $"Phone Quality: {PhoneQuality}";
        AudioEnabled = PhoneAudioOn;
        StatusMessage = $"PHONE CAMERA READY | Video: {(PhoneVideoOn ? "ON" : "OFF")} | Audio: {(PhoneAudioOn ? "ON" : "OFF")} | Quality: {PhoneQuality}";
        VideoPreviewState = "VIDEO ACTIVE";
        VideoPreviewStats = $"Resolution: {ResolveResolutionForQuality(PhoneQuality)} | FPS: {ResolveFpsForQuality(PhoneQuality)}";
    }

    private void ApplyCameraStoppedSignal()
    {
        PhoneVideoOn = false;
        PhoneAudioOn = false;
        PhoneQuality = "N/A";
        DeviceName = "Phone connected";
        StatusMessage = "PHONE CAMERA STOPPED";
        VideoPreviewState = "RECEIVING VIDEO...";
        VideoPreviewStats = "Resolution: N/A | FPS: N/A";
    }

    public void LoadFromAppSettings(AppSettings appSettings)
    {
        RemoteWebAppUrl = string.IsNullOrWhiteSpace(appSettings.RemoteWebAppUrl)
            ? "https://titan-webcam.vercel.app"
            : appSettings.RemoteWebAppUrl.Trim();
        RemoteSignalingServerUrl = string.IsNullOrWhiteSpace(appSettings.RemoteSignalingServerUrl)
            ? "https://camera.titanaudio.vn"
            : appSettings.RemoteSignalingServerUrl.Trim();
        SyncConfigToSettings();
    }

    public void SaveToAppSettings(AppSettings appSettings)
    {
        appSettings.RemoteWebAppUrl = RemoteWebAppUrl;
        appSettings.RemoteSignalingServerUrl = RemoteSignalingServerUrl;
    }

    private void OpenVideoPreview()
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            var previewUrl = _webRtcReceiver.BuildPreviewUrl(_session, _settings);
            var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            var window = new RemoteCameraPreviewWindow(previewUrl) { Owner = owner };
            window.Show();
            VideoPreviewState = "RECEIVING VIDEO...";
        }
        catch
        {
            VideoPreviewState = "SIGNAL LOST";
        }
    }

    private static string ResolveResolutionForQuality(string quality) =>
        quality switch
        {
            "HD" => "1920x1080",
            "LOW" or "SAFE_5G" or "SAFE 5G" => "1280x720",
            _ => "N/A",
        };

    private static string ResolveFpsForQuality(string quality) =>
        quality switch
        {
            "HD" or "LOW" => "30",
            "SAFE_5G" or "SAFE 5G" => "24",
            _ => "N/A",
        };

    private void SyncConfigToSettings()
    {
        _settings.WebAppBaseUrl = RemoteWebAppUrl;
        _settings.SignalingServerUrl = RemoteSignalingServerUrl;
    }

    private void SaveConfig()
    {
        if (!RemoteWebAppUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            ServerTestStatus = "Web App URL invalid. Use https://";
            return;
        }

        if (!(RemoteSignalingServerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
              RemoteSignalingServerUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
        {
            ServerTestStatus = "Signaling URL invalid.";
            return;
        }

        SyncConfigToSettings();
        ServerTestStatus = "CONFIG SAVED";
    }

    private async Task TestServerAsync()
    {
        try
        {
            SyncConfigToSettings();
            var ok = await _signalingClient.HealthCheckAsync(RemoteSignalingServerUrl);
            ServerTestStatus = ok ? "SERVER ONLINE" : "SERVER OFFLINE / CHECK URL";
        }
        catch
        {
            ServerTestStatus = "SERVER OFFLINE / CHECK URL";
        }
    }
}
