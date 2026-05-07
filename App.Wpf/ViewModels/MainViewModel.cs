using System.Collections.ObjectModel;
using System.Globalization;
using System.Media;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TitanAILivePC.Core;
using TitanAILivePC.Models;
using TitanAILivePC.Services;

namespace TitanAILivePC.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly Brush LiveTrafficGreen = CreateFrozenBrush("#52C08A");
    private static readonly Brush LiveTrafficYellow = CreateFrozenBrush("#E8B84C");
    private static readonly Brush LiveTrafficRed = CreateFrozenBrush("#E05858");
    private static readonly Brush LiveTrafficMuted = CreateFrozenBrush("#5C6B82");
    private static readonly Brush LiveTrafficCyan = CreateFrozenBrush("#6FD6E8");
    private static readonly Brush LiveTrafficAmber = CreateFrozenBrush("#F5C542");
    private static readonly Brush LiveTrafficOrange = CreateFrozenBrush("#E8A065");
    private static readonly Brush LiveTrafficMint = CreateFrozenBrush("#5CDE8A");
    private static readonly Brush LiveBroadcastOnAirRed = CreateFrozenBrush("#D26464");
    private static readonly Brush LiveBroadcastVoiceCyan = CreateFrozenBrush("#67D4EA");

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private readonly AiReplyService _aiReplyService = new();
    private readonly OverlayHttpServer _overlayHttpServer = new();
    private readonly TextToSpeechService _textToSpeechService = new();
    private readonly ObsWebSocketService _obsWebSocketService = new();
    private readonly OcrChatCaptureService _ocrChatCaptureService = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly DispatcherTimer _uiTimer = new();
    private readonly DispatcherTimer _studioClockTimer = new();
    private readonly Random _random = new();
    private int _commentCount;

    private string _commentInput = string.Empty;
    private string _currentReply = "AI reply will appear here.";
    private string _openAiApiKey = string.Empty;
    private string _overlayStatus = "Overlay server is stopped.";
    private string _latestCommentPreview = "No comments yet.";
    private string _obsHost = "localhost";
    private string _obsPort = "4455";
    private string _obsPassword = string.Empty;
    private string _obsStatus = "OBS not connected.";
    private string _selectedObsScene = string.Empty;
    private string _selectedObsSource = string.Empty;
    private string _obsOverlaySourceName = "Titan AI Overlay";
    private string _obsTextSourceName = "Titan AI Reply";
    private bool _isMuted;
    private bool _isLivePulsing;
    private bool _isLiveStreaming;
    private bool _isObsConnected;
    private bool _isAiOnline = true;
    private bool _isAiEngineEnabled = true;
    private bool _isAiSpeaking;
    private bool _autoSpeakLiveReply = true;
    private double _backgroundMediaVolume = 100;
    private double _cpuUsage = 22;
    private double _ramUsage = 41;
    private string _streamInfo = "1920x1080 | 60 FPS | 6.5 Mbps";
    private string _currentActiveScene = "Titan_Main";
    private string _ttsStatus = "Idle";
    private double _aiSensitivity = 62;
    private double _voiceDepth = 48;
    private double _replySpeed = 57;
    private double _overlayOpacity = 82;
    private double _ttsGain = 64;
    private double _peakLeft = 38;
    private double _peakRight = 44;
    private double _peakHoldLeft = 38;
    private double _peakHoldRight = 44;
    private double _rmsLevel = 34;
    private Rect? _selectedChatRegion;
    private string _ocrStatus = "OCR idle.";
    private string _lastDetectedComment = "None";
    private bool _autoReplyEnabled = true;
    private bool _autoTtsEnabled = true;
    private bool _isOcrRunning;
    private string _blacklistInput = "spam,badword";
    private string _ocrCooldownSeconds = "3";
    private string _ocrSetupStatus = "OCR setup unknown.";
    private bool _isLowLatencyMode;
    private bool _isLiveReady;
    private bool _isDspEngineEnabled;
    private bool _isSmartResponseEnabled = true;
    private int _ttsDelayMs = 220;
    private string _selectedVoiceName = "vi-VN-HoaiMyNeural";
    private string _pendingVoiceName = "vi-VN-HoaiMyNeural";
    private double _voiceSpeed = 1.0;
    private double _voicePitch = 1.0;
    private string _selectedVoiceStylePreset = "Greeting";
    private bool _autoVoiceStylePreset = true;
    private bool _isApplyingAutoVoicePreset;
    private AiReplyType _lastReplyType = AiReplyType.Greeting;
    private string _currentVoicePresetStatusText = "CURRENT PRESET: GREETING (AUTO)";
    private Brush _currentVoicePresetBadgeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5CE1E6"));
    private bool _isManualApproveMode;
    private PendingApprovalItem? _pendingApproval;
    private bool _isPendingAlertActive;
    private bool _beepOnPending = true;
    private CancellationTokenSource? _pendingAlertCts;
    private string _ocrConfidenceThreshold = "40";
    private readonly Queue<SpeakQueueItem> _speakQueue = [];
    private readonly Dictionary<string, DateTime> _recentSpokenCommentKeys = [];
    private bool _isProcessingSpeakQueue;
    private DateTime _lastSpokenReplyAt = DateTime.MinValue;
    private CancellationTokenSource? _currentSpeakCts;
    private string _ttsEngineBadgeText = "TTS ENGINE: EDGE VIETNAMESE";
    private Brush _ttsEngineBadgeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5CE1E6"));
    private string _ttsEngineTooltip = "Voice: vi-VN-HoaiMyNeural";
    private string _ttsErrorMessage = string.Empty;
    private Visibility _ttsErrorVisibility = Visibility.Collapsed;
    private Func<Task<Rect?>>? _chatRegionSelector;
    private UiOperationMode _currentUiMode = UiOperationMode.Live;
    private Func<Task<bool>>? _engineerModePasswordGate;
    private int _replyGenerationDepth;
    private LiveAiState _currentLiveAiState = LiveAiState.Idle;
    private string _lastViewerCommentForPersonality = string.Empty;
    private DateTime _lastLiveAiBeatUtc = DateTime.UtcNow;
    private TextToSpeechService.TtsEngineState _ttsRouteEngineState = TextToSpeechService.TtsEngineState.EdgeVietnamese;
    private LiveBroadcastState _currentBroadcastState = LiveBroadcastState.Standby;
    private DateTime? _liveSessionStartUtc;
    private string _broadcastClockText = "00:00:00";
    private string _broadcastSessionClockDigits = "00:00:00";
    private double _liveStudioAudioEnergy;
    private StartupPhase _currentStartupPhase = StartupPhase.Booting;
    private LiveSetupStep _currentLiveSetupStep = LiveSetupStep.ConnectObs;
    private bool _isOverlayVisibleInProgram;
    private int _safeInitializeGate;
    private bool _dashboardTimersStarted;
    private int _uiPulseTick;
    private string _overlayBrandName = "TITAN AUDIO VIETNAM";
    private string _overlayBrandFontPreset = "Broadcast Bold";

    // TODO: move to protected config later
    private const string EngineerModePassword = "titanpro";

    public MainViewModel()
    {
        RemoteCamera = new RemoteCameraViewModel();

        AddCommentCommand = new RelayCommand(AddComment);
        GenerateReplyCommand = new RelayCommand(async () => await GenerateReplyAsync());
        StartOverlayServerCommand = new RelayCommand(StartOverlayServer);
        StopOverlayServerCommand = new RelayCommand(StopOverlayServer);
        SpeakReplyCommand = new RelayCommand(SpeakReply);
        ConnectObsCommand = new RelayCommand(async () => await ConnectObsAsync());
        ReconnectObsCommand = new RelayCommand(async () => await ReconnectObsAsync());
        ToggleLiveCommand = new RelayCommand(async () => await ToggleLiveAsync());
        ToggleAiEngineCommand = new RelayCommand(ToggleAiEngine);
        DisconnectObsCommand = new RelayCommand(DisconnectObs);
        RefreshObsScenesCommand = new RelayCommand(async () => await RefreshObsScenesAsync());
        SwitchObsSceneCommand = new RelayCommand(async () => await SwitchObsSceneAsync());
        RefreshObsSourcesCommand = new RelayCommand(async () => await RefreshObsSourcesAsync());
        ShowOverlaySourceCommand = new RelayCommand(async () => await ToggleOverlaySourceAsync(true));
        HideOverlaySourceCommand = new RelayCommand(async () => await ToggleOverlaySourceAsync(false));
        PushReplyToObsTextCommand = new RelayCommand(async () => await PushReplyToObsTextAsync());
        SelectChatRegionCommand = new RelayCommand(async () => await SelectChatRegionAsync());
        StartOcrCommand = new RelayCommand(StartOcrCapture);
        StopOcrCommand = new RelayCommand(StopOcrCapture);
        CheckOcrSetupCommand = new RelayCommand(CheckOcrSetup);
        OpenTessdataFolderCommand = new RelayCommand(OpenTessdataFolder);
        ValidateLiveReadyCommand = new RelayCommand(ValidateLiveReady);
        ToggleDspToolsCommand = new RelayCommand(ToggleDspToolsPanel);
        PreviewVoiceCommand = new RelayCommand(async () => await PreviewVoiceAsync());
        ApplyVoiceSelectionCommand = new RelayCommand(ApplyPendingVoiceSelection, () => CanApplyPendingVoiceSelection);
        TestEdgeVietnameseCommand = new RelayCommand(async () => await TestEdgeVietnameseAsync());
        ApproveAndSpeakCommand = new RelayCommand(async () => await ApproveAndSpeakAsync(), () => PendingApproval is not null);
        ApproveAndPushObsCommand = new RelayCommand(async () => await ApproveAndPushObsAsync(), () => PendingApproval is not null);
        IgnorePendingCommand = new RelayCommand(IgnorePendingApproval, () => PendingApproval is not null);
        ToggleEngineerModeCommand = new RelayCommand(async () => await ToggleEngineerModeAsync());

        PlaceholderComments.Add("viewer091: Shop nay co VF5 mau den khong?");
        PlaceholderComments.Add("viewer214: Cong suat dau day anh?");
        PlaceholderComments.Add("viewer303: Co ship nhanh trong ngay khong?");
        PlaceholderComments.Add("viewer517: Em xin gia tot nhat cho 2 cap.");

        _uiTimer.Tick += (_, _) => UpdateRealtimeUi();
        _studioClockTimer.Tick += (_, _) => OnStudioClockTick();

        _ocrChatCaptureService.StatusChanged += status =>
            EnqueueUiSafe(() => OcrStatus = status);
        _ocrChatCaptureService.MessageDetected += message =>
        {
            var d = Application.Current?.Dispatcher;
            if (d is null || d.HasShutdownStarted)
            {
                return;
            }

            _ = d.InvokeAsync(async () => await ProcessDetectedCommentAsync(message));
        };
        _ocrChatCaptureService.DebugLog += log =>
            EnqueueUiSafeLowPriority(() =>
                AddLog(log, log.StartsWith("REJECTED REASON:", StringComparison.Ordinal) ? "WARN" : "INFO"));
        _textToSpeechService.DebugLog += log =>
            EnqueueUiSafeLowPriority(() =>
                AddLog(
                    log,
                    log.StartsWith("EDGE TTS FAILED:", StringComparison.Ordinal) ||
                    log.Contains("FALLBACK", StringComparison.Ordinal) ||
                    log.Contains("not installed", StringComparison.OrdinalIgnoreCase)
                        ? "WARN"
                        : "INFO"));
        _textToSpeechService.EngineStateChanged += (state, selectedVoice, lastError) =>
            EnqueueUiSafe(() => UpdateTtsEngineBadge(state, selectedVoice, lastError));

        foreach (var voice in _textToSpeechService.VietnameseVoices)
        {
            VoiceOptions.Add(voice);
        }

        foreach (var preset in VoiceStylePresets)
        {
            VoiceStylePresetOptions.Add(preset);
        }

        _obsHost = _settings.ObsHost;
        _obsPort = _settings.ObsPort.ToString(CultureInfo.InvariantCulture);
        _obsPassword = _settings.ObsPassword;
        _isMuted = _settings.IsMuted;
        _overlayBrandName = string.IsNullOrWhiteSpace(_settings.OverlayBrandName)
            ? "TITAN AUDIO VIETNAM"
            : _settings.OverlayBrandName;
        _overlayBrandFontPreset = string.IsNullOrWhiteSpace(_settings.OverlayBrandFontPreset)
            ? "Broadcast Bold"
            : _settings.OverlayBrandFontPreset;
        _overlayHttpServer.SetBrandName(_overlayBrandName);
        _overlayHttpServer.SetBrandFontPreset(_overlayBrandFontPreset);
        _selectedVoiceName = _settings.VoiceName;
        _pendingVoiceName = _selectedVoiceName;
        _voiceSpeed = _settings.VoiceSpeed;
        _voicePitch = _settings.VoicePitch;
        _selectedVoiceStylePreset = _settings.VoiceStylePreset;
        _autoVoiceStylePreset = _settings.AutoVoiceStylePreset;
        _isAiOnline = !string.IsNullOrWhiteSpace(_settings.OpenAiApiKey);
        _openAiApiKey = _settings.OpenAiApiKey;
        RemoteCamera.LoadFromAppSettings(_settings);

        AddBootLog("[BOOT] Constructor ready");
    }

    public void SaveAppSettings()
    {
        if (int.TryParse(ObsPort, out var port) && port > 0)
        {
            _settings.ObsPort = port;
        }

        RemoteCamera.SaveToAppSettings(_settings);
        _settings.Save();
        AddLog("Settings saved.");
    }

    public void ResetAppToDefaults()
    {
        var defaults = new AppSettings();
        OpenAiApiKey = defaults.OpenAiApiKey;
        ObsHost = defaults.ObsHost;
        ObsPort = defaults.ObsPort.ToString(CultureInfo.InvariantCulture);
        ObsPassword = defaults.ObsPassword;
        IsMuted = defaults.IsMuted;
        SelectedVoiceName = defaults.VoiceName;
        PendingVoiceName = defaults.VoiceName;
        VoiceSpeed = defaults.VoiceSpeed;
        VoicePitch = defaults.VoicePitch;
        SelectedVoiceStylePreset = defaults.VoiceStylePreset;
        AutoVoiceStylePreset = defaults.AutoVoiceStylePreset;
        OverlayBrandName = defaults.OverlayBrandName;
        OverlayBrandFontPreset = defaults.OverlayBrandFontPreset;
        RemoteCamera.LoadFromAppSettings(defaults);
        SaveAppSettings();
        AddLog("Reset to default settings complete.");
    }

    /// <summary>Called from MainWindow Loaded — marks UI chrome ready before deferred init.</summary>
    public void SetStartupPhaseUiReady()
    {
        if (_currentStartupPhase != StartupPhase.Booting)
        {
            return;
        }

        CurrentStartupPhase = StartupPhase.UiReady;
        AddBootLog("[BOOT] Window loaded");
    }

    /// <summary>Deferred startup: timers, OCR probe, waveforms, personality — after first render.</summary>
    public async Task SafeInitializeAsync()
    {
        if (Interlocked.Exchange(ref _safeInitializeGate, 1) != 0)
        {
            return;
        }

        AddBootLog("[BOOT] Safe init begin");
        CurrentStartupPhase = StartupPhase.BackgroundInitializing;
        await Task.Yield();

        AddBootLog("[BOOT] Voices enum (background thread)");
        string voiceDbg;
        try
        {
            voiceDbg = await Task.Run(() => _textToSpeechService.GetInstalledVoicesDebugText())
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            voiceDbg = $"TTS VOICES: enumeration skipped ({ex.Message})";
            AddBootLog($"[BOOT] Voice enum exception: {ex.Message}");
        }

        AddLog(voiceDbg);
        AddLog("TTS DEFAULT EDGE VOICE: vi-VN-HoaiMyNeural");

        CheckOcrSetup();
        AddBootLog("[BOOT] OCR ready");

        for (var i = 0; i < 28; i++)
        {
            WaveformBars.Add(8 + _random.NextDouble() * 24);
        }

        for (var i = 0; i < 26; i++)
        {
            LiveSpeakingWaveBars.Add(8 + _random.NextDouble() * 12);
        }

        ApplyVoiceStylePreset(_selectedVoiceStylePreset, fromAutoSelection: false);
        RefreshVoicePresetStatus(logChange: false);
        ApplyVoiceSettings();

        RaisePropertyChanged(nameof(IsAiOnline));
        RaisePropertyChanged(nameof(OpenAiApiKey));
        RaisePropertyChanged(nameof(SelectedVoiceName));
        RaisePropertyChanged(nameof(PendingVoiceName));
        RaisePropertyChanged(nameof(CanApplyPendingVoiceSelection));
        RaisePropertyChanged(nameof(VoiceSpeed));
        RaisePropertyChanged(nameof(VoicePitch));
        RaisePropertyChanged(nameof(SelectedVoiceStylePreset));
        RaisePropertyChanged(nameof(AutoVoiceStylePreset));

        StartDashboardTimers();
        AddBootLog("[BOOT] Timers ready");

        CurrentStartupPhase = StartupPhase.Running;
        RaiseLivePersonalityUi();
        RefreshBroadcastStudioState();
        RefreshLiveModePresentation();
        AddBootLog("[BOOT] Running");
    }

    private void StartDashboardTimers()
    {
        if (_dashboardTimersStarted)
        {
            return;
        }

        _dashboardTimersStarted = true;
        _uiTimer.Interval = TimeSpan.FromMilliseconds(280);
        _studioClockTimer.Interval = TimeSpan.FromSeconds(1);
        _uiTimer.Start();
        _studioClockTimer.Start();
    }

    private static void EnqueueUiSafe(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }

    private static void EnqueueUiSafeLowPriority(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }

    private void AddBootLog(string message) => AddLog(message);

    public ObservableCollection<LiveComment> Comments { get; } = new();
    public ObservableCollection<LogEntry> Logs { get; } = new();
    public ObservableCollection<double> WaveformBars { get; } = new();
    public ObservableCollection<string> PlaceholderComments { get; } = new();
    public ObservableCollection<string> ObsScenes { get; } = new();
    public ObservableCollection<string> ObsSources { get; } = new();
    public ObservableCollection<string> VoiceOptions { get; } = new();
    public ObservableCollection<string> VoiceStylePresetOptions { get; } = new();
    public ObservableCollection<string> OverlayBrandFontPresetOptions { get; } =
    [
        "Broadcast Bold",
        "Tech Condensed",
        "Elegant Serif",
        "Neon Clean",
    ];
    public ObservableCollection<double> LiveSpeakingWaveBars { get; } = new();
    public RemoteCameraViewModel RemoteCamera { get; }

    public RelayCommand AddCommentCommand { get; }
    public RelayCommand GenerateReplyCommand { get; }
    public RelayCommand StartOverlayServerCommand { get; }
    public RelayCommand StopOverlayServerCommand { get; }
    public RelayCommand SpeakReplyCommand { get; }
    public RelayCommand ConnectObsCommand { get; }
    public RelayCommand ReconnectObsCommand { get; }
    public RelayCommand ToggleLiveCommand { get; }
    public RelayCommand ToggleAiEngineCommand { get; }
    public RelayCommand DisconnectObsCommand { get; }
    public RelayCommand RefreshObsScenesCommand { get; }
    public RelayCommand SwitchObsSceneCommand { get; }
    public RelayCommand RefreshObsSourcesCommand { get; }
    public RelayCommand ShowOverlaySourceCommand { get; }
    public RelayCommand HideOverlaySourceCommand { get; }
    public RelayCommand PushReplyToObsTextCommand { get; }
    public RelayCommand SelectChatRegionCommand { get; }
    public RelayCommand StartOcrCommand { get; }
    public RelayCommand StopOcrCommand { get; }
    public RelayCommand CheckOcrSetupCommand { get; }
    public RelayCommand OpenTessdataFolderCommand { get; }
    public RelayCommand ValidateLiveReadyCommand { get; }
    public RelayCommand ToggleDspToolsCommand { get; }
    public RelayCommand PreviewVoiceCommand { get; }
    public RelayCommand ApplyVoiceSelectionCommand { get; }
    public RelayCommand TestEdgeVietnameseCommand { get; }
    public RelayCommand ApproveAndSpeakCommand { get; }
    public RelayCommand ApproveAndPushObsCommand { get; }
    public RelayCommand IgnorePendingCommand { get; }
    public RelayCommand ToggleEngineerModeCommand { get; }

    public UiOperationMode CurrentUiMode
    {
        get => _currentUiMode;
        set
        {
            if (SetProperty(ref _currentUiMode, value))
            {
                RaisePropertyChanged(nameof(IsEngineerMode));
                RaisePropertyChanged(nameof(IsLiveMode));
                RaisePropertyChanged(nameof(ShowLiveStartupOverlay));
                RaisePropertyChanged(nameof(IsConnectObsSetupTarget));
                RaisePropertyChanged(nameof(IsStartOverlaySetupTarget));
                RaisePropertyChanged(nameof(IsShowOverlaySetupTarget));
                RaisePropertyChanged(nameof(IsSelectChatRegionSetupTarget));
                RaisePropertyChanged(nameof(IsStartOcrSetupTarget));
                RaisePropertyChanged(nameof(IsConnectObsSetupCompleted));
                RaisePropertyChanged(nameof(IsStartOverlaySetupCompleted));
                RaisePropertyChanged(nameof(IsShowOverlaySetupCompleted));
                RaisePropertyChanged(nameof(IsSelectChatRegionSetupCompleted));
                RaisePropertyChanged(nameof(IsStartOcrSetupCompleted));
                RaisePropertyChanged(nameof(LiveSetupStatusText));
                RaisePropertyChanged(nameof(LiveSetupInstructionText));
                RefreshLiveModePresentation();
                RefreshLiveSetupWizardState();
            }
        }
    }

    public bool IsEngineerMode => CurrentUiMode == UiOperationMode.Engineer;

    public bool IsLiveMode => CurrentUiMode == UiOperationMode.Live;

    public bool IsOverlayVisibleInProgram => _isOverlayVisibleInProgram;

    public LiveSetupStep CurrentLiveSetupStep
    {
        get => _currentLiveSetupStep;
        private set
        {
            if (!SetProperty(ref _currentLiveSetupStep, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(LiveSetupStatusText));
            RaisePropertyChanged(nameof(LiveSetupInstructionText));
            RaisePropertyChanged(nameof(IsConnectObsSetupTarget));
            RaisePropertyChanged(nameof(IsStartOverlaySetupTarget));
            RaisePropertyChanged(nameof(IsShowOverlaySetupTarget));
            RaisePropertyChanged(nameof(IsSelectChatRegionSetupTarget));
            RaisePropertyChanged(nameof(IsStartOcrSetupTarget));
            RaisePropertyChanged(nameof(IsConnectObsSetupCompleted));
            RaisePropertyChanged(nameof(IsStartOverlaySetupCompleted));
            RaisePropertyChanged(nameof(IsShowOverlaySetupCompleted));
            RaisePropertyChanged(nameof(IsSelectChatRegionSetupCompleted));
            RaisePropertyChanged(nameof(IsStartOcrSetupCompleted));
        }
    }

    public string LiveSetupStatusText => CurrentLiveSetupStep switch
    {
        LiveSetupStep.ConnectObs => "STEP 1 READY",
        LiveSetupStep.StartOverlay => "STEP 2 READY",
        LiveSetupStep.ShowOverlay => "STEP 3 READY",
        LiveSetupStep.SelectChatRegion => "STEP 4 READY",
        LiveSetupStep.StartOcr => "STEP 5 READY",
        LiveSetupStep.ReadyToLive => "TITAN AI LIVE READY",
        _ => "STEP 1 READY",
    };

    public string LiveSetupInstructionText => CurrentLiveSetupStep switch
    {
        LiveSetupStep.ConnectObs => "Bước 1: Bấm CONNECT OBS để kết nối OBS.",
        LiveSetupStep.StartOverlay => "Bước 2: Bấm START OVERLAY để mở overlay Titan.",
        LiveSetupStep.ShowOverlay => "Bước 3: Bấm SHOW để hiện overlay lên chương trình.",
        LiveSetupStep.SelectChatRegion => "Bước 4: Bấm SELECT CHAT REGION để chọn vùng bình luận.",
        LiveSetupStep.StartOcr => "Bước 5: Bấm START OCR để bắt đầu đọc bình luận.",
        LiveSetupStep.ReadyToLive => "TITAN AI LIVE READY",
        _ => "Bước 1: Bấm CONNECT OBS để kết nối OBS.",
    };

    public bool IsConnectObsSetupTarget => IsLiveMode && CurrentLiveSetupStep == LiveSetupStep.ConnectObs;
    public bool IsStartOverlaySetupTarget => IsLiveMode && CurrentLiveSetupStep == LiveSetupStep.StartOverlay;
    public bool IsShowOverlaySetupTarget => IsLiveMode && CurrentLiveSetupStep == LiveSetupStep.ShowOverlay;
    public bool IsSelectChatRegionSetupTarget => IsLiveMode && CurrentLiveSetupStep == LiveSetupStep.SelectChatRegion;
    public bool IsStartOcrSetupTarget => IsLiveMode && CurrentLiveSetupStep == LiveSetupStep.StartOcr;

    public bool IsConnectObsSetupCompleted => IsLiveMode && (IsObsConnected || CurrentLiveSetupStep == LiveSetupStep.ReadyToLive);
    public bool IsStartOverlaySetupCompleted => IsLiveMode && (_overlayHttpServer.IsRunning || CurrentLiveSetupStep == LiveSetupStep.ReadyToLive);
    public bool IsShowOverlaySetupCompleted => IsLiveMode && (_isOverlayVisibleInProgram || CurrentLiveSetupStep == LiveSetupStep.ReadyToLive);
    public bool IsSelectChatRegionSetupCompleted => IsLiveMode && (_selectedChatRegion.HasValue || CurrentLiveSetupStep == LiveSetupStep.ReadyToLive);
    public bool IsStartOcrSetupCompleted => IsLiveMode && (IsOcrRunning || CurrentLiveSetupStep == LiveSetupStep.ReadyToLive);

    public StartupPhase CurrentStartupPhase
    {
        get => _currentStartupPhase;
        private set
        {
            if (!SetProperty(ref _currentStartupPhase, value, nameof(CurrentStartupPhase)))
            {
                return;
            }

            RaisePropertyChanged(nameof(ShowLiveStartupOverlay));
            RaisePropertyChanged(nameof(IsLiveBroadcastEffectsEnabled));
            RaisePropertyChanged(nameof(IsLiveAiEnergyPulseForChrome));
            RaisePropertyChanged(nameof(IsLiveAiSpeakingWaveformForChrome));
        }
    }

    public bool ShowLiveStartupOverlay =>
        IsLiveMode && _currentStartupPhase != StartupPhase.Running;

    public bool IsLiveBroadcastEffectsEnabled =>
        _currentStartupPhase == StartupPhase.Running;

    public LiveAiState CurrentLiveAiState => _currentLiveAiState;

    public string LiveAiStateText => FormatLiveAiStateLine(_currentLiveAiState);

    public Brush LiveAiStateBrush => BrushForLiveAiState(_currentLiveAiState);

    /// <summary>Role headline under TITAN AI (Live Mode broadcast strip).</summary>
    public string LiveAiRoleHeadline => _currentLiveAiState switch
    {
        LiveAiState.ClosingSale => "TITAN CLOSING DESK",
        LiveAiState.HotlineMode => "TITAN TECHNICAL SUPPORT",
        LiveAiState.TechnicalMode => "TITAN TECHNICAL SUPPORT",
        _ => "TITAN AI ON AIR",
    };

    public bool IsLiveAiEnergyPulse => _currentLiveAiState is LiveAiState.Thinking
        or LiveAiState.Speaking
        or LiveAiState.ClosingSale;

    public bool IsLiveAiSpeakingWaveform =>
        _currentLiveAiState == LiveAiState.Speaking && IsLiveAiSpeakingStatus(TtsStatus);

    public bool IsLiveAiEnergyPulseForChrome =>
        IsLiveBroadcastEffectsEnabled &&
        (_currentLiveAiState is LiveAiState.Thinking or LiveAiState.Speaking or LiveAiState.ClosingSale);

    public bool IsLiveAiSpeakingWaveformForChrome =>
        IsLiveBroadcastEffectsEnabled &&
        _currentLiveAiState == LiveAiState.Speaking &&
        IsLiveAiSpeakingStatus(TtsStatus);

    /// <summary>Legacy binding kept in sync with personality line.</summary>
    public string BroadcastPresenterPhase => LiveAiStateText.Replace("● ", "", StringComparison.Ordinal).Trim();

    public Brush BroadcastPresenterPhaseBrush => LiveAiStateBrush;

    public bool IsBroadcastAiEnergyActive => IsLiveAiEnergyPulse;

    public string LiveFooterVoiceRouteCaption => _ttsRouteEngineState switch
    {
        TextToSpeechService.TtsEngineState.EdgeVietnamese => "● VIETNAMESE NEURAL VOICE",
        TextToSpeechService.TtsEngineState.WebVietnamese => "● WEB VIETNAMESE FALLBACK",
        _ => "● VOICE ROUTE CHECK",
    };

    public Brush LiveFooterVoiceRouteBrush => _ttsRouteEngineState switch
    {
        TextToSpeechService.TtsEngineState.EdgeVietnamese => LiveTrafficGreen,
        TextToSpeechService.TtsEngineState.WebVietnamese => LiveTrafficYellow,
        _ => LiveTrafficRed,
    };

    public bool IsOverlayServerRunning => _overlayHttpServer.IsRunning;

    public string LiveOverlayHeadline => _overlayHttpServer.IsRunning ? "ON AIR" : "STANDBY";

    public string LiveHeaderAiCaption => AutoReplyEnabled && IsAiEngineEnabled ? "● AI ACTIVE" : "● AI IDLE";

    public Brush LiveHeaderAiBrush => AutoReplyEnabled && IsAiEngineEnabled ? LiveTrafficGreen : LiveTrafficMuted;

    public string LiveHeaderOcrCaption => IsOcrRunning ? "● OCR ACTIVE" : "● OCR OFF";

    public Brush LiveHeaderOcrBrush => IsOcrRunning ? LiveTrafficGreen : LiveTrafficMuted;

    public string LiveHeaderObsCaption => IsObsConnected ? "● OBS LIVE" : "● OBS OFF";

    public Brush LiveHeaderObsBrush => IsObsConnected ? LiveTrafficGreen : LiveTrafficRed;

    public string LiveObsConnectionHint
    {
        get
        {
            if (IsObsConnected)
            {
                return "OBS đã kết nối. Có thể qua bước tiếp theo.";
            }

            if (ObsStatus.Contains("invalid password", StringComparison.OrdinalIgnoreCase))
            {
                return "OBS WebSocket: sai password. Kiểm tra Tools > WebSocket Server Settings.";
            }

            if (ObsStatus.Contains("disabled", StringComparison.OrdinalIgnoreCase))
            {
                return "OBS WebSocket chưa bật. Vào Tools > WebSocket Server Settings và bật server.";
            }

            if (ObsStatus.Contains("may not be open", StringComparison.OrdinalIgnoreCase) ||
                ObsStatus.Contains("not connected", StringComparison.OrdinalIgnoreCase))
            {
                return "Mở OBS và bật WebSocket server (thử port 4455, nếu plugin cũ thì 4444), rồi bấm CONNECT OBS.";
            }

            return ObsStatus;
        }
    }

    public string LiveHeaderReadyCaption => _overlayHttpServer.IsRunning ? "● PROGRAM LIVE" : IsLiveReady ? "● LIVE READY" : "● SETUP";

    public Brush LiveHeaderReadyBrush => _overlayHttpServer.IsRunning ? LiveTrafficCyan : IsLiveReady ? LiveTrafficGreen : LiveTrafficYellow;

    public string LiveFooterCpuCaption => $"● CPU {CpuUsage:F0}%";

    public Brush LiveFooterCpuBrush =>
        CpuUsage >= 90 ? LiveTrafficRed : CpuUsage >= 72 ? LiveTrafficYellow : LiveTrafficGreen;

    public string LiveFooterRamCaption => $"● RAM {RamUsage:F0}%";

    public Brush LiveFooterRamBrush =>
        RamUsage >= 90 ? LiveTrafficRed : RamUsage >= 75 ? LiveTrafficYellow : LiveTrafficGreen;

    public string LiveFooterOcrCaption
    {
        get
        {
            if (IsOcrRunning)
            {
                return "● OCR ONLINE";
            }

            return OcrSetupStatus.Contains("sẵn sàng", StringComparison.OrdinalIgnoreCase)
                ? "● OCR STANDBY"
                : "● OCR SETUP";
        }
    }

    public Brush LiveFooterOcrBrush
    {
        get
        {
            if (IsOcrRunning)
            {
                return LiveTrafficGreen;
            }

            return OcrSetupStatus.Contains("sẵn sàng", StringComparison.OrdinalIgnoreCase)
                ? LiveTrafficYellow
                : LiveTrafficRed;
        }
    }

    public string LiveFooterTtsCaption
    {
        get
        {
            if (TtsErrorVisibility == Visibility.Visible)
            {
                return "● VOICE CHECK";
            }

            if (IsMuted)
            {
                return "● TTS MUTED";
            }

            return string.Equals(TtsStatus, "Speaking", StringComparison.OrdinalIgnoreCase)
                ? "● TTS LIVE"
                : _textToSpeechService.IsReady
                    ? "● TTS READY"
                    : "● TTS INIT";
        }
    }

    public Brush LiveFooterTtsBrush
    {
        get
        {
            if (TtsErrorVisibility == Visibility.Visible)
            {
                return LiveTrafficRed;
            }

            if (IsMuted)
            {
                return LiveTrafficYellow;
            }

            return string.Equals(TtsStatus, "Speaking", StringComparison.OrdinalIgnoreCase)
                ? LiveTrafficCyan
                : _textToSpeechService.IsReady
                    ? LiveTrafficGreen
                    : LiveTrafficYellow;
        }
    }

    public string LiveFooterObsCaption => IsObsConnected ? "● OBS CONNECTED" : "● OBS DOWN";

    public Brush LiveFooterObsBrush => IsObsConnected ? LiveTrafficGreen : LiveTrafficRed;

    public string LiveFooterOverlayCaption => _overlayHttpServer.IsRunning ? "● OVERLAY LIVE" : "● OVERLAY OFF";

    public Brush LiveFooterOverlayBrush => _overlayHttpServer.IsRunning ? LiveTrafficGreen : LiveTrafficMuted;

    public LiveBroadcastState CurrentBroadcastState => _currentBroadcastState;

    public string BroadcastClockText => _broadcastClockText;

    public string BroadcastSessionClockDigits => _broadcastSessionClockDigits;

    public string BroadcastSessionTitleLine =>
        _liveSessionStartUtc.HasValue
            ? $"LIVE SESSION  {_broadcastSessionClockDigits}"
            : "SESSION OFF AIR";

    public string BroadcastStateText => FormatBroadcastStateLine(_currentBroadcastState);

    public Brush BroadcastStateBrush => BrushForBroadcastState(_currentBroadcastState);

    public string BroadcastHeroBadgeText => FormatBroadcastHeroLine(_currentBroadcastState);

    public string BroadcastFooterSmartLine => BuildBroadcastFooterSmartLine();

    public string LiveProgramOutputLine =>
        IsLiveAiSpeakingStatus(TtsStatus) ? "PROGRAM OUTPUT LIVE" : "PROGRAM READY";

    public bool IsLiveProgramOutputLive => IsLiveAiSpeakingStatus(TtsStatus);

    public double LiveStudioAudioEnergy
    {
        get => _liveStudioAudioEnergy;
        private set => SetProperty(ref _liveStudioAudioEnergy, value);
    }

    public string CommentInput
    {
        get => _commentInput;
        set => SetProperty(ref _commentInput, value);
    }

    public string CurrentReply
    {
        get => _currentReply;
        set => SetProperty(ref _currentReply, value);
    }

    public string OpenAiApiKey
    {
        get => _openAiApiKey;
        set
        {
            if (SetProperty(ref _openAiApiKey, value))
            {
                _settings.OpenAiApiKey = value;
                IsAiOnline = !string.IsNullOrWhiteSpace(value);
                RaisePropertyChanged(nameof(AiBadgeText));
                RaisePropertyChanged(nameof(AiBadgeBackgroundBrush));
                RaisePropertyChanged(nameof(AiBadgeBorderBrush));
            }
        }
    }

    public string OverlayStatus
    {
        get => _overlayStatus;
        set => SetProperty(ref _overlayStatus, value);
    }

    public string LatestCommentPreview
    {
        get => _latestCommentPreview;
        set => SetProperty(ref _latestCommentPreview, value);
    }

    public string ObsHost
    {
        get => _obsHost;
        set
        {
            if (SetProperty(ref _obsHost, value))
            {
                _settings.ObsHost = value;
            }
        }
    }

    public string ObsPort
    {
        get => _obsPort;
        set => SetProperty(ref _obsPort, value);
    }

    public string ObsPassword
    {
        get => _obsPassword;
        set
        {
            if (SetProperty(ref _obsPassword, value))
            {
                _settings.ObsPassword = value;
            }
        }
    }

    public string ObsStatus
    {
        get => _obsStatus;
        set
        {
            if (SetProperty(ref _obsStatus, value))
            {
                RaisePropertyChanged(nameof(LiveObsConnectionHint));
            }
        }
    }

    public string SelectedObsScene
    {
        get => _selectedObsScene;
        set => SetProperty(ref _selectedObsScene, value);
    }

    public string SelectedObsSource
    {
        get => _selectedObsSource;
        set => SetProperty(ref _selectedObsSource, value);
    }

    public string ObsOverlaySourceName
    {
        get => _obsOverlaySourceName;
        set => SetProperty(ref _obsOverlaySourceName, value);
    }

    public string ObsTextSourceName
    {
        get => _obsTextSourceName;
        set => SetProperty(ref _obsTextSourceName, value);
    }

    public string OverlayBrandName
    {
        get => _overlayBrandName;
        set
        {
            if (SetProperty(ref _overlayBrandName, value))
            {
                _settings.OverlayBrandName = value;
                _overlayHttpServer.SetBrandName(value);
            }
        }
    }

    public string OverlayBrandFontPreset
    {
        get => _overlayBrandFontPreset;
        set
        {
            if (SetProperty(ref _overlayBrandFontPreset, value))
            {
                _settings.OverlayBrandFontPreset = value;
                _overlayHttpServer.SetBrandFontPreset(value);
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                _settings.IsMuted = value;
                _textToSpeechService.SetMuted(value);
                TtsStatus = value ? "Muted" : "Idle";
                AddLog(value ? "TTS muted." : "TTS unmuted.");
                RefreshLiveModePresentation();
            }
        }
    }

    public bool IsLivePulsing
    {
        get => _isLivePulsing;
        set => SetProperty(ref _isLivePulsing, value);
    }

    public bool IsLiveStreaming
    {
        get => _isLiveStreaming;
        set
        {
            if (SetProperty(ref _isLiveStreaming, value))
            {
                RaisePropertyChanged(nameof(LiveBadgeText));
                RaisePropertyChanged(nameof(LiveBadgeBackgroundBrush));
                RaisePropertyChanged(nameof(LiveBadgeBorderBrush));
            }
        }
    }

    public bool IsObsConnected
    {
        get => _isObsConnected;
        set
        {
            if (SetProperty(ref _isObsConnected, value))
            {
                RaisePropertyChanged(nameof(ObsBadgeText));
                RaisePropertyChanged(nameof(ObsBadgeBackgroundBrush));
                RaisePropertyChanged(nameof(ObsBadgeBorderBrush));
                RaisePropertyChanged(nameof(IsConnectObsSetupCompleted));
                RefreshBroadcastStudioState();
                RefreshLiveSetupWizardState();
            }
        }
    }

    public bool IsAiOnline
    {
        get => _isAiOnline;
        set
        {
            if (SetProperty(ref _isAiOnline, value))
            {
                RaisePropertyChanged(nameof(AiBadgeText));
                RaisePropertyChanged(nameof(AiBadgeBackgroundBrush));
                RaisePropertyChanged(nameof(AiBadgeBorderBrush));
            }
        }
    }

    public bool IsAiEngineEnabled
    {
        get => _isAiEngineEnabled;
        set
        {
            if (SetProperty(ref _isAiEngineEnabled, value))
            {
                RaisePropertyChanged(nameof(AiBadgeText));
                RaisePropertyChanged(nameof(AiBadgeBackgroundBrush));
                RaisePropertyChanged(nameof(AiBadgeBorderBrush));
                RefreshLiveModePresentation();
            }
        }
    }

    public bool IsAiSpeaking
    {
        get => _isAiSpeaking;
        set
        {
            if (SetProperty(ref _isAiSpeaking, value))
            {
                RaisePropertyChanged(nameof(IsVoiceLiveIndicatorActive));
            }
        }
    }

    public bool AutoSpeakLiveReply
    {
        get => _autoSpeakLiveReply;
        set => SetProperty(ref _autoSpeakLiveReply, value);
    }

    public double BackgroundMediaVolume
    {
        get => _backgroundMediaVolume;
        set => SetProperty(ref _backgroundMediaVolume, value);
    }

    public double CpuUsage
    {
        get => _cpuUsage;
        set => SetProperty(ref _cpuUsage, value);
    }

    public double RamUsage
    {
        get => _ramUsage;
        set => SetProperty(ref _ramUsage, value);
    }

    public string StreamInfo
    {
        get => _streamInfo;
        set => SetProperty(ref _streamInfo, value);
    }

    public string CurrentActiveScene
    {
        get => _currentActiveScene;
        set => SetProperty(ref _currentActiveScene, value);
    }

    public string TtsStatus
    {
        get => _ttsStatus;
        set
        {
            var prev = _ttsStatus;
            if (!SetProperty(ref _ttsStatus, value))
            {
                return;
            }

            if (IsLiveAiSpeakingStatus(value))
            {
                ApplyLiveAiState(LiveAiState.Speaking);
            }
            else if (IsLiveAiSpeakingStatus(prev))
            {
                ApplyLiveAiState(LivePersonalityIntent.ClassifyFromViewerComment(_lastViewerCommentForPersonality));
            }
            else
            {
                RaiseLivePersonalityUi();
            }

            RaisePropertyChanged(nameof(LiveFooterTtsCaption));
            RaisePropertyChanged(nameof(LiveFooterTtsBrush));
            RaisePropertyChanged(nameof(IsLiveAiSpeakingWaveform));
            RaisePropertyChanged(nameof(IsLiveAiSpeakingWaveformForChrome));
            RefreshBroadcastStudioState();
        }
    }

    public string OcrStatus
    {
        get => _ocrStatus;
        set => SetProperty(ref _ocrStatus, value);
    }

    public string LastDetectedComment
    {
        get => _lastDetectedComment;
        set => SetProperty(ref _lastDetectedComment, value);
    }

    public bool AutoReplyEnabled
    {
        get => _autoReplyEnabled;
        set
        {
            if (SetProperty(ref _autoReplyEnabled, value))
            {
                RefreshLiveModePresentation();
            }
        }
    }

    public bool AutoTtsEnabled
    {
        get => _autoTtsEnabled;
        set => SetProperty(ref _autoTtsEnabled, value);
    }

    public bool IsOcrRunning
    {
        get => _isOcrRunning;
        set
        {
            if (SetProperty(ref _isOcrRunning, value))
            {
                RaisePropertyChanged(nameof(IsStartOcrSetupCompleted));
                RefreshLiveModePresentation();
                RefreshLiveSetupWizardState();
            }
        }
    }

    public string BlacklistInput
    {
        get => _blacklistInput;
        set => SetProperty(ref _blacklistInput, value);
    }

    public string OcrCooldownSeconds
    {
        get => _ocrCooldownSeconds;
        set => SetProperty(ref _ocrCooldownSeconds, value);
    }

    public string OcrConfidenceThreshold
    {
        get => _ocrConfidenceThreshold;
        set => SetProperty(ref _ocrConfidenceThreshold, value);
    }

    public string OcrSetupStatus
    {
        get => _ocrSetupStatus;
        set
        {
            if (SetProperty(ref _ocrSetupStatus, value))
            {
                RefreshLiveModePresentation();
            }
        }
    }

    public bool IsLowLatencyMode
    {
        get => _isLowLatencyMode;
        set
        {
            if (SetProperty(ref _isLowLatencyMode, value))
            {
                ApplyPerformanceProfile();
            }
        }
    }

    public bool IsLiveReady
    {
        get => _isLiveReady;
        set
        {
            if (SetProperty(ref _isLiveReady, value))
            {
                RefreshLiveModePresentation();
            }
        }
    }

    public bool IsDspEngineEnabled
    {
        get => _isDspEngineEnabled;
        set => SetProperty(ref _isDspEngineEnabled, value);
    }

    public bool IsSmartResponseEnabled
    {
        get => _isSmartResponseEnabled;
        set
        {
            if (SetProperty(ref _isSmartResponseEnabled, value))
            {
                _aiReplyService.UseShortReplyMode = value;
                AddLog(value ? "Smart response mode: short livestream replies." : "Smart response mode: long detailed replies.");
            }
        }
    }

    public string SelectedVoiceName
    {
        get => _selectedVoiceName;
        set
        {
            if (SetProperty(ref _selectedVoiceName, value))
            {
                _settings.VoiceName = value;
                ApplyVoiceSettings();
                if (!string.Equals(_pendingVoiceName, value, StringComparison.Ordinal))
                {
                    _pendingVoiceName = value;
                    RaisePropertyChanged(nameof(PendingVoiceName));
                }
                RaisePropertyChanged(nameof(CanApplyPendingVoiceSelection));
                ApplyVoiceSelectionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string PendingVoiceName
    {
        get => _pendingVoiceName;
        set
        {
            if (SetProperty(ref _pendingVoiceName, value))
            {
                RaisePropertyChanged(nameof(CanApplyPendingVoiceSelection));
                ApplyVoiceSelectionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanApplyPendingVoiceSelection =>
        !string.IsNullOrWhiteSpace(PendingVoiceName) &&
        !string.Equals(PendingVoiceName, SelectedVoiceName, StringComparison.Ordinal);

    public double VoiceSpeed
    {
        get => _voiceSpeed;
        set
        {
            if (SetProperty(ref _voiceSpeed, value))
            {
                _settings.VoiceSpeed = value;
                ApplyVoiceSettings();
            }
        }
    }

    public double VoicePitch
    {
        get => _voicePitch;
        set
        {
            if (SetProperty(ref _voicePitch, value))
            {
                _settings.VoicePitch = value;
                ApplyVoiceSettings();
            }
        }
    }

    public string SelectedVoiceStylePreset
    {
        get => _selectedVoiceStylePreset;
        set
        {
            if (SetProperty(ref _selectedVoiceStylePreset, value))
            {
                _settings.VoiceStylePreset = value;
                ApplyVoiceStylePreset(value, fromAutoSelection: _isApplyingAutoVoicePreset);
                RefreshVoicePresetStatus(logChange: true);
            }
        }
    }

    public bool AutoVoiceStylePreset
    {
        get => _autoVoiceStylePreset;
        set
        {
            if (SetProperty(ref _autoVoiceStylePreset, value))
            {
                _settings.AutoVoiceStylePreset = value;
                RefreshVoicePresetStatus(logChange: true);
            }
        }
    }

    public string CurrentVoicePresetStatusText
    {
        get => _currentVoicePresetStatusText;
        set => SetProperty(ref _currentVoicePresetStatusText, value);
    }

    public Brush CurrentVoicePresetBadgeBrush
    {
        get => _currentVoicePresetBadgeBrush;
        set => SetProperty(ref _currentVoicePresetBadgeBrush, value);
    }

    public bool IsManualApproveMode
    {
        get => _isManualApproveMode;
        set
        {
            if (SetProperty(ref _isManualApproveMode, value))
            {
                AddLog(value ? "MANUAL APPROVE MODE enabled." : "MANUAL APPROVE MODE disabled.");
                RaisePropertyChanged(nameof(IsAiActiveChipPulseActive));
            }
        }
    }

    public PendingApprovalItem? PendingApproval
    {
        get => _pendingApproval;
        set
        {
            if (SetProperty(ref _pendingApproval, value))
            {
                RaiseApprovalCommandsCanExecuteChanged();
                RaisePropertyChanged(nameof(PendingCount));
                RaisePropertyChanged(nameof(PendingCountText));
                RaisePropertyChanged(nameof(IsAiActiveChipPulseActive));
            }
        }
    }

    public bool IsPendingAlertActive
    {
        get => _isPendingAlertActive;
        set => SetProperty(ref _isPendingAlertActive, value);
    }

    public bool BeepOnPending
    {
        get => _beepOnPending;
        set => SetProperty(ref _beepOnPending, value);
    }

    public int PendingCount => PendingApproval is null ? 0 : 1;

    public string PendingCountText => $"PENDING: {PendingCount}";

    public bool IsAiActiveChipPulseActive => IsManualApproveMode && PendingApproval is not null;
    public bool IsVoiceLiveIndicatorActive => IsAiSpeaking;
    public string TtsEngineBadgeText
    {
        get => _ttsEngineBadgeText;
        set => SetProperty(ref _ttsEngineBadgeText, value);
    }

    public Brush TtsEngineBadgeBrush
    {
        get => _ttsEngineBadgeBrush;
        set => SetProperty(ref _ttsEngineBadgeBrush, value);
    }

    public string TtsEngineTooltip
    {
        get => _ttsEngineTooltip;
        set => SetProperty(ref _ttsEngineTooltip, value);
    }

    public string TtsErrorMessage
    {
        get => _ttsErrorMessage;
        set => SetProperty(ref _ttsErrorMessage, value);
    }

    public Visibility TtsErrorVisibility
    {
        get => _ttsErrorVisibility;
        set
        {
            if (SetProperty(ref _ttsErrorVisibility, value))
            {
                RefreshLiveModePresentation();
            }
        }
    }

    public string LiveBadgeText => IsLiveStreaming ? "LIVE ON" : "LIVE OFF";

    public Brush LiveBadgeBackgroundBrush => IsLiveStreaming
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A1418"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A202A"));

    public Brush LiveBadgeBorderBrush => IsLiveStreaming
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5A5A"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#55647A"));

    public string ObsBadgeText => IsObsConnected ? "OBS CONNECTED" : "OBS DISCONNECTED";

    public Brush ObsBadgeBackgroundBrush => IsObsConnected
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#162A1C"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A1418"));

    public Brush ObsBadgeBorderBrush => IsObsConnected
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#73D08B"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C65F6B"));

    public string AiBadgeText => !IsAiEngineEnabled
        ? "AI DISABLED"
        : IsAiOnline ? "AI ONLINE" : "AI LOCAL";

    public Brush AiBadgeBackgroundBrush => !IsAiEngineEnabled
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A202A"))
        : IsAiOnline
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#152B1D"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E2413"));

    public Brush AiBadgeBorderBrush => !IsAiEngineEnabled
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6A778C"))
        : IsAiOnline
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#73D08B"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0A34A"));

    public double AiSensitivity
    {
        get => _aiSensitivity;
        set => SetProperty(ref _aiSensitivity, value);
    }

    public double VoiceDepth
    {
        get => _voiceDepth;
        set => SetProperty(ref _voiceDepth, value);
    }

    public double ReplySpeed
    {
        get => _replySpeed;
        set => SetProperty(ref _replySpeed, value);
    }

    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set => SetProperty(ref _overlayOpacity, value);
    }

    public double TtsGain
    {
        get => _ttsGain;
        set => SetProperty(ref _ttsGain, value);
    }

    public double PeakLeft
    {
        get => _peakLeft;
        set => SetProperty(ref _peakLeft, value);
    }

    public double PeakRight
    {
        get => _peakRight;
        set => SetProperty(ref _peakRight, value);
    }

    public double PeakHoldLeft
    {
        get => _peakHoldLeft;
        set => SetProperty(ref _peakHoldLeft, value);
    }

    public double PeakHoldRight
    {
        get => _peakHoldRight;
        set => SetProperty(ref _peakHoldRight, value);
    }

    public double RmsLevel
    {
        get => _rmsLevel;
        set => SetProperty(ref _rmsLevel, value);
    }

    public void SetChatRegionSelector(Func<Task<Rect?>> selector)
    {
        _chatRegionSelector = selector;
    }

    public void SetEngineerModePasswordGate(Func<Task<bool>>? gate)
    {
        _engineerModePasswordGate = gate;
    }

    private static bool IsLiveAiSpeakingStatus(string? tts) =>
        string.Equals(tts, "Speaking", StringComparison.OrdinalIgnoreCase);

    private static string FormatLiveAiStateLine(LiveAiState state) =>
        state switch
        {
            LiveAiState.Idle => "● READY",
            LiveAiState.Listening => "● LISTENING",
            LiveAiState.Thinking => "● THINKING",
            LiveAiState.Speaking => "● SPEAKING",
            LiveAiState.HotlineMode => "● HOTLINE SUPPORT",
            LiveAiState.ClosingSale => "● CLOSING SALE",
            LiveAiState.TechnicalMode => "● TECHNICAL SUPPORT",
            _ => "● READY",
        };

    private Brush BrushForLiveAiState(LiveAiState state) =>
        state switch
        {
            LiveAiState.Thinking => LiveTrafficCyan,
            LiveAiState.Speaking => LiveTrafficGreen,
            LiveAiState.HotlineMode => LiveTrafficAmber,
            LiveAiState.TechnicalMode => LiveTrafficOrange,
            LiveAiState.ClosingSale => LiveTrafficMint,
            LiveAiState.Listening => LiveTrafficCyan,
            LiveAiState.Idle => LiveTrafficMuted,
            _ => LiveTrafficMuted,
        };

    private void ApplyLiveAiState(LiveAiState state, bool bumpActivityClock = true)
    {
        _currentLiveAiState = state;
        if (bumpActivityClock)
        {
            _lastLiveAiBeatUtc = DateTime.UtcNow;
        }

        RaiseLivePersonalityUi();
    }

    private void RaiseLivePersonalityUi()
    {
        if (_currentStartupPhase != StartupPhase.Running)
        {
            return;
        }

        RaisePropertyChanged(nameof(CurrentLiveAiState));
        RaisePropertyChanged(nameof(LiveAiStateText));
        RaisePropertyChanged(nameof(LiveAiStateBrush));
        RaisePropertyChanged(nameof(LiveAiRoleHeadline));
        RaisePropertyChanged(nameof(IsLiveAiEnergyPulse));
        RaisePropertyChanged(nameof(IsLiveAiSpeakingWaveform));
        RaisePropertyChanged(nameof(IsLiveAiEnergyPulseForChrome));
        RaisePropertyChanged(nameof(IsLiveAiSpeakingWaveformForChrome));
        RaisePropertyChanged(nameof(BroadcastPresenterPhase));
        RaisePropertyChanged(nameof(BroadcastPresenterPhaseBrush));
        RaisePropertyChanged(nameof(IsBroadcastAiEnergyActive));
    }

    private void PushReplyGeneration()
    {
        _replyGenerationDepth++;
        ApplyLiveAiState(LiveAiState.Thinking);
    }

    private void FinishReplyGeneration(string? viewerComment)
    {
        _replyGenerationDepth = Math.Max(0, _replyGenerationDepth - 1);
        if (_replyGenerationDepth == 0 && !IsLiveAiSpeakingStatus(TtsStatus))
        {
            ApplyLiveAiState(LivePersonalityIntent.ClassifyFromViewerComment(viewerComment));
            return;
        }

        RaiseLivePersonalityUi();
    }

    private void TryDecayLiveAiToIdle()
    {
        if (_replyGenerationDepth > 0)
        {
            return;
        }

        if (IsLiveAiSpeakingStatus(TtsStatus))
        {
            return;
        }

        if (_currentLiveAiState == LiveAiState.Idle)
        {
            return;
        }

        if ((DateTime.UtcNow - _lastLiveAiBeatUtc).TotalSeconds < 4.5)
        {
            return;
        }

        ApplyLiveAiState(LiveAiState.Idle, bumpActivityClock: false);
    }

    private void OnStudioClockTick()
    {
        if (_currentStartupPhase != StartupPhase.Running)
        {
            return;
        }

        _broadcastClockText = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        RaisePropertyChanged(nameof(BroadcastClockText));
        UpdateLiveSessionBaseline();
        UpdateBroadcastSessionDigits();
        RaisePropertyChanged(nameof(BroadcastSessionClockDigits));
        RaisePropertyChanged(nameof(BroadcastSessionTitleLine));
        RefreshBroadcastStudioState();
    }

    private void UpdateLiveSessionBaseline()
    {
        var active = IsOcrRunning || _overlayHttpServer.IsRunning;
        if (active)
        {
            _liveSessionStartUtc ??= DateTime.UtcNow;
            return;
        }

        _liveSessionStartUtc = null;
    }

    private void UpdateBroadcastSessionDigits()
    {
        if (!_liveSessionStartUtc.HasValue)
        {
            _broadcastSessionClockDigits = "00:00:00";
            return;
        }

        var elapsed = DateTime.UtcNow - _liveSessionStartUtc.Value;
        var hours = (int)elapsed.TotalHours;
        _broadcastSessionClockDigits = $"{hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }

    private void RefreshBroadcastStudioState()
    {
        if (_currentStartupPhase != StartupPhase.Running)
        {
            return;
        }

        var next = ResolveBroadcastState();
        var changed = next != _currentBroadcastState;
        _currentBroadcastState = next;
        RaiseBroadcastStudioProps(changed);
    }

    private LiveBroadcastState ResolveBroadcastState()
    {
        if (!IsObsConnected)
        {
            return LiveBroadcastState.Offline;
        }

        if (IsMuted)
        {
            return LiveBroadcastState.Muted;
        }

        if (IsLiveAiSpeakingStatus(TtsStatus))
        {
            return LiveBroadcastState.Speaking;
        }

        if (IsOcrRunning)
        {
            return LiveBroadcastState.Live;
        }

        return LiveBroadcastState.Standby;
    }

    private static string FormatBroadcastStateLine(LiveBroadcastState state) =>
        state switch
        {
            LiveBroadcastState.Standby => "● STANDBY",
            LiveBroadcastState.Live => "● LIVE",
            LiveBroadcastState.Speaking => "● VOICE LIVE",
            LiveBroadcastState.Muted => "● MUTED",
            LiveBroadcastState.Offline => "● OFFLINE",
            _ => "● STANDBY",
        };

    private static string FormatBroadcastHeroLine(LiveBroadcastState state) =>
        state switch
        {
            LiveBroadcastState.Standby => "STANDBY",
            LiveBroadcastState.Live => "ON AIR",
            LiveBroadcastState.Speaking => "VOICE LIVE",
            LiveBroadcastState.Muted => "MUTED",
            LiveBroadcastState.Offline => "OFFLINE",
            _ => "STANDBY",
        };

    private Brush BrushForBroadcastState(LiveBroadcastState state) =>
        state switch
        {
            LiveBroadcastState.Live => LiveBroadcastOnAirRed,
            LiveBroadcastState.Speaking => LiveBroadcastVoiceCyan,
            LiveBroadcastState.Standby => LiveTrafficMuted,
            LiveBroadcastState.Muted => LiveTrafficAmber,
            LiveBroadcastState.Offline => LiveTrafficRed,
            _ => LiveTrafficMuted,
        };

    private string BuildBroadcastFooterSmartLine()
    {
        return _currentBroadcastState switch
        {
            LiveBroadcastState.Offline => "● PROGRAM OFFLINE · ROUTE STANDBY",
            LiveBroadcastState.Muted => "● STUDIO OUTPUT MUTED",
            LiveBroadcastState.Speaking => "● TITAN VOICE ONLINE · PROGRAM BUS HOT",
            LiveBroadcastState.Live => "● OBS PROGRAM LIVE · OCR STREAM ACTIVE",
            LiveBroadcastState.Standby => "● WAITING FOR COMMENT · AI RESPONSE READY",
            _ => "● STANDBY",
        };
    }

    private void RaiseBroadcastStudioProps(bool stateEnumChanged)
    {
        if (stateEnumChanged)
        {
            RaisePropertyChanged(nameof(CurrentBroadcastState));
        }

        RaisePropertyChanged(nameof(BroadcastStateText));
        RaisePropertyChanged(nameof(BroadcastStateBrush));
        RaisePropertyChanged(nameof(BroadcastHeroBadgeText));
        RaisePropertyChanged(nameof(BroadcastFooterSmartLine));
        RaisePropertyChanged(nameof(LiveProgramOutputLine));
        RaisePropertyChanged(nameof(IsLiveProgramOutputLive));
        RaisePropertyChanged(nameof(BroadcastSessionTitleLine));
    }

    private static void PrepareCommentInboundSpotlight(ObservableCollection<LiveComment> comments, LiveComment newest)
    {
        foreach (var c in comments)
        {
            c.IsLatestInbound = false;
        }

        newest.IsLatestInbound = true;
    }

    private static void MarkCommentAiHandled(LiveComment? comment)
    {
        if (comment is null)
        {
            return;
        }

        comment.HasAiHandled = true;
    }

    private void RefreshLiveFooterMetersOnly()
    {
        RaisePropertyChanged(nameof(LiveFooterCpuCaption));
        RaisePropertyChanged(nameof(LiveFooterCpuBrush));
        RaisePropertyChanged(nameof(LiveFooterRamCaption));
        RaisePropertyChanged(nameof(LiveFooterRamBrush));
    }

    private void RefreshLiveModePresentation()
    {
        RaisePropertyChanged(nameof(IsOverlayServerRunning));
        RaisePropertyChanged(nameof(LiveOverlayHeadline));
        RaisePropertyChanged(nameof(LiveHeaderAiCaption));
        RaisePropertyChanged(nameof(LiveHeaderAiBrush));
        RaisePropertyChanged(nameof(LiveHeaderOcrCaption));
        RaisePropertyChanged(nameof(LiveHeaderOcrBrush));
        RaisePropertyChanged(nameof(LiveHeaderObsCaption));
        RaisePropertyChanged(nameof(LiveHeaderObsBrush));
        RaisePropertyChanged(nameof(LiveHeaderReadyCaption));
        RaisePropertyChanged(nameof(LiveHeaderReadyBrush));
        RaisePropertyChanged(nameof(LiveFooterCpuCaption));
        RaisePropertyChanged(nameof(LiveFooterCpuBrush));
        RaisePropertyChanged(nameof(LiveFooterRamCaption));
        RaisePropertyChanged(nameof(LiveFooterRamBrush));
        RaisePropertyChanged(nameof(LiveFooterOcrCaption));
        RaisePropertyChanged(nameof(LiveFooterOcrBrush));
        RaisePropertyChanged(nameof(LiveFooterTtsCaption));
        RaisePropertyChanged(nameof(LiveFooterTtsBrush));
        RaisePropertyChanged(nameof(LiveFooterObsCaption));
        RaisePropertyChanged(nameof(LiveFooterObsBrush));
        RaisePropertyChanged(nameof(LiveFooterOverlayCaption));
        RaisePropertyChanged(nameof(LiveFooterOverlayBrush));
        RaisePropertyChanged(nameof(LiveFooterVoiceRouteCaption));
        RaisePropertyChanged(nameof(LiveFooterVoiceRouteBrush));
        RaisePropertyChanged(nameof(IsConnectObsSetupCompleted));
        RaisePropertyChanged(nameof(IsStartOverlaySetupCompleted));
        RaisePropertyChanged(nameof(IsShowOverlaySetupCompleted));
        RaisePropertyChanged(nameof(IsSelectChatRegionSetupCompleted));
        RaisePropertyChanged(nameof(IsStartOcrSetupCompleted));
        RaisePropertyChanged(nameof(LiveSetupStatusText));
        RaisePropertyChanged(nameof(LiveSetupInstructionText));
        if (_currentStartupPhase == StartupPhase.Running)
        {
            RaiseLivePersonalityUi();
            RefreshBroadcastStudioState();
        }

        RefreshLiveSetupWizardState();
    }

    private void RefreshLiveSetupWizardState()
    {
        CurrentLiveSetupStep = ResolveLiveSetupStep();
    }

    private LiveSetupStep ResolveLiveSetupStep()
    {
        if (!IsLiveMode)
        {
            return LiveSetupStep.ReadyToLive;
        }

        if (!IsObsConnected)
        {
            return LiveSetupStep.ConnectObs;
        }

        if (!_overlayHttpServer.IsRunning)
        {
            return LiveSetupStep.StartOverlay;
        }

        if (!_selectedChatRegion.HasValue)
        {
            if (!_isOverlayVisibleInProgram)
            {
                return LiveSetupStep.ShowOverlay;
            }

            return LiveSetupStep.SelectChatRegion;
        }

        if (!IsOcrRunning)
        {
            return LiveSetupStep.StartOcr;
        }

        return LiveSetupStep.ReadyToLive;
    }

    private void SetOverlayVisibleInProgram(bool visible)
    {
        if (_isOverlayVisibleInProgram == visible)
        {
            return;
        }

        _isOverlayVisibleInProgram = visible;
        RaisePropertyChanged(nameof(IsOverlayVisibleInProgram));
        RaisePropertyChanged(nameof(IsShowOverlaySetupCompleted));
        RefreshLiveSetupWizardState();
    }

    public bool VerifyEngineerPassword(string? password) =>
        string.Equals(password, EngineerModePassword, StringComparison.Ordinal);

    private async Task ToggleEngineerModeAsync()
    {
        if (CurrentUiMode == UiOperationMode.Engineer)
        {
            CurrentUiMode = UiOperationMode.Live;
            return;
        }

        if (_engineerModePasswordGate is null)
        {
            return;
        }

        var ok = await _engineerModePasswordGate();
        if (ok)
        {
            CurrentUiMode = UiOperationMode.Engineer;
        }
    }

    private void AddComment()
    {
        if (string.IsNullOrWhiteSpace(CommentInput))
        {
            AddLog("Cannot add empty comment.");
            return;
        }

        _commentCount++;
        var comment = new LiveComment
        {
            UserName = $"viewer{_commentCount:000}",
            CommentText = CommentInput.Trim(),
            ConfidenceScore = 100,
            Timestamp = DateTime.Now
        };

        Comments.Insert(0, comment);
        PrepareCommentInboundSpotlight(Comments, comment);
        LatestCommentPreview = $"{comment.UserName}: {comment.CommentText}";
        _lastViewerCommentForPersonality = comment.CommentText;
        ApplyLiveAiState(LiveAiState.Listening);
        _overlayHttpServer.UpdateData(comment, CurrentReply);
        CurrentActiveScene = "Comment_Interaction";
        AddLog($"Comment added from {comment.UserName}.");
        CommentInput = string.Empty;
    }

    private async Task GenerateReplyAsync()
    {
        if (!IsAiEngineEnabled)
        {
            AddLog("AI engine is disabled. Enable AI ONLINE to generate reply.", "WARN");
            return;
        }

        var latestComment = Comments.FirstOrDefault();
        _lastViewerCommentForPersonality = latestComment?.CommentText ?? string.Empty;
        if (latestComment is not null)
        {
            ApplyLiveAiState(LiveAiState.Listening);
        }

        await Task.Yield();

        PushReplyGeneration();
        AiReplyResult? replyResult = null;
        try
        {
            replyResult = await _aiReplyService.GenerateReplyAsync(OpenAiApiKey, latestComment);
        }
        finally
        {
            FinishReplyGeneration(latestComment?.CommentText);
        }

        if (replyResult is null)
        {
            return;
        }

        _lastReplyType = replyResult.ReplyType;
        ApplyAutoVoicePreset(_lastReplyType);
        CurrentReply = replyResult.ReplyText;
        MarkCommentAiHandled(latestComment);
        IsAiOnline = !string.IsNullOrWhiteSpace(OpenAiApiKey);
        CurrentActiveScene = "AI_Response";
        _overlayHttpServer.UpdateData(latestComment, CurrentReply);
        if (IsManualApproveMode)
        {
            var approvalComment = latestComment ?? new LiveComment
            {
                UserName = "manual_user",
                CommentText = LatestCommentPreview
            };
            QueuePendingApproval(approvalComment, replyResult);
        }
        else
        {
            await TrySyncObsTextAsync(CurrentReply);
        }
        AddLog(string.IsNullOrWhiteSpace(OpenAiApiKey)
            ? "Generated reply in offline demo mode."
            : "Generated reply via OpenAI.");
    }

    private void StartOverlayServer()
    {
        try
        {
            _overlayHttpServer.Start(message => AddLog(message));
            SetOverlayVisibleInProgram(false);
            OverlayStatus = $"Overlay running at {_overlayHttpServer.Url}";
            CurrentActiveScene = "Overlay_Live";
            AddLog("Overlay server started.");
            RaisePropertyChanged(nameof(IsOverlayServerRunning));
            RaisePropertyChanged(nameof(LiveOverlayHeadline));
            RefreshLiveModePresentation();
        }
        catch (Exception ex)
        {
            OverlayStatus = "Overlay failed to start.";
            AddLog($"Overlay start error: {ex.Message}", "ERROR");
            RaisePropertyChanged(nameof(IsOverlayServerRunning));
            RaisePropertyChanged(nameof(LiveOverlayHeadline));
            RefreshLiveModePresentation();
        }
    }

    private void StopOverlayServer()
    {
        _overlayHttpServer.Stop();
        SetOverlayVisibleInProgram(false);
        OverlayStatus = "Overlay server is stopped.";
        CurrentActiveScene = "Titan_Main";
        AddLog("Overlay server stopped.");
        RaisePropertyChanged(nameof(IsOverlayServerRunning));
        RaisePropertyChanged(nameof(LiveOverlayHeadline));
        RefreshLiveModePresentation();
    }

    private async void SpeakReply()
    {
        var speechText = PrepareReplyForSpeech(CurrentReply);
        if (!IsMuted && !string.IsNullOrWhiteSpace(CurrentReply))
        {
            IsAiSpeaking = true;
            TtsStatus = "Speaking";
        }

        var warning = await _textToSpeechService.SpeakAsync(speechText);
        IsAiSpeaking = false;
        TtsStatus = IsMuted ? "Muted" : "Idle";
        if (!string.IsNullOrWhiteSpace(warning))
        {
            AddLog(warning, "WARN");
        }
        AddLog(IsMuted ? "Speak skipped because TTS is muted." : "Speaking current reply.");
    }

    private async Task ConnectObsAsync()
    {
        ObsStatus = "Connecting OBS...";
        if (!int.TryParse(ObsPort, out var port))
        {
            ObsStatus = "OBS port is invalid.";
            IsObsConnected = false;
            AddLog("OBS connect failed: invalid port.", "WARN");
            return;
        }

        _settings.ObsPort = port;
        var host = string.IsNullOrWhiteSpace(ObsHost) ? "localhost" : ObsHost.Trim();
        var connectStatus = await _obsWebSocketService.ConnectAsync(host, port, ObsPassword);
        ObsStatus = connectStatus;

        if (!_obsWebSocketService.IsConnected &&
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
            !ObsStatus.Contains("invalid password", StringComparison.OrdinalIgnoreCase))
        {
            // Some machines resolve localhost oddly; quick fallback retry.
            var retryStatus = await _obsWebSocketService.ConnectAsync("127.0.0.1", port, ObsPassword);
            ObsStatus = retryStatus;
            if (_obsWebSocketService.IsConnected)
            {
                ObsHost = "127.0.0.1";
            }
        }

        if (!_obsWebSocketService.IsConnected &&
            !ObsStatus.Contains("invalid password", StringComparison.OrdinalIgnoreCase))
        {
            // OBS websocket v4 commonly uses 4444.
            var fallbackPort = port == 4455 ? 4444 : 4455;
            var fallbackStatus = await _obsWebSocketService.ConnectAsync(ObsHost, fallbackPort, ObsPassword);
            if (_obsWebSocketService.IsConnected)
            {
                ObsPort = fallbackPort.ToString(CultureInfo.InvariantCulture);
                ObsStatus = $"{fallbackStatus} (auto fallback port {fallbackPort})";
            }
        }

        var statusLooksConnected =
            ObsStatus.Contains("connected successfully", StringComparison.OrdinalIgnoreCase) ||
            ObsStatus.Contains("already connected", StringComparison.OrdinalIgnoreCase);
        IsObsConnected = _obsWebSocketService.IsConnected || statusLooksConnected;

        if (IsObsConnected)
        {
            var streamState = await _obsWebSocketService.GetStreamingStatusAsync();
            // Do not downgrade OBS connection based on stream-status RPC.
            // Some OBS setups can connect successfully but fail this query transiently.
            if (streamState.Status.StartsWith("OBS not connected", StringComparison.OrdinalIgnoreCase))
            {
                IsLiveStreaming = false;
                AddLog("OBS connected, but streaming status query failed. Keeping OBS connected state.", "WARN");
            }
            else
            {
                IsLiveStreaming = streamState.IsStreaming;
            }
        }
        else
        {
            SetOverlayVisibleInProgram(false);
            IsLiveStreaming = false;
            ShowObsConnectErrorDialog(ObsStatus);
        }
        if (IsObsConnected)
        {
            CurrentActiveScene = "Program_Live";
            await RefreshObsScenesAsync();
        }
        AddLog(ObsStatus);
    }

    private void ShowObsConnectErrorDialog(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            status = "OBS connection failed.";
        }

        var detail = status;
        if (status.Contains("invalid password", StringComparison.OrdinalIgnoreCase))
        {
            detail += "\n\nKiểm tra OBS > Tools > WebSocket Server Settings > Server Password.";
        }
        else if (status.Contains("may not be open", StringComparison.OrdinalIgnoreCase) ||
                 status.Contains("disabled", StringComparison.OrdinalIgnoreCase) ||
                 status.Contains("not connected", StringComparison.OrdinalIgnoreCase))
        {
            detail += "\n\nKiểm tra:\n- OBS đang mở\n- WebSocket server đã bật\n- Port đúng (4455 hoặc 4444)\n- Host đúng (localhost hoặc 127.0.0.1)";
        }

        MessageBox.Show(
            detail,
            "OBS Connection Error",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private async Task ReconnectObsAsync()
    {
        if (_obsWebSocketService.IsConnected)
        {
            _obsWebSocketService.Disconnect();
        }

        await ConnectObsAsync();
        AddLog("OBS RECONNECTED");
    }

    private void DisconnectObs()
    {
        ObsStatus = _obsWebSocketService.Disconnect();
        IsObsConnected = _obsWebSocketService.IsConnected;
        SetOverlayVisibleInProgram(false);
        IsLiveStreaming = false;
        ObsScenes.Clear();
        ObsSources.Clear();
        AddLog(ObsStatus);
    }

    private async Task ToggleLiveAsync()
    {
        if (!IsObsConnected)
        {
            AddLog("Cannot toggle LIVE: OBS not connected.", "WARN");
            return;
        }

        var shouldStart = !IsLiveStreaming;
        var status = IsLiveStreaming
            ? await _obsWebSocketService.StopStreamAsync()
            : await _obsWebSocketService.StartStreamAsync();

        var streamState = await _obsWebSocketService.GetStreamingStatusAsync();
        IsLiveStreaming = streamState.IsStreaming;
        ObsStatus = status;
        if (shouldStart && IsLiveStreaming)
        {
            AddLog("LIVE STARTED");
            return;
        }

        if (!shouldStart && !IsLiveStreaming)
        {
            AddLog("LIVE STOPPED");
            return;
        }

        AddLog(status, "WARN");
    }

    private void ToggleAiEngine()
    {
        IsAiEngineEnabled = !IsAiEngineEnabled;
        AddLog(IsAiEngineEnabled ? "AI ENABLED" : "AI DISABLED");
    }

    private async Task RefreshObsScenesAsync()
    {
        var scenes = await _obsWebSocketService.GetScenesAsync();
        ObsScenes.Clear();
        foreach (var scene in scenes)
        {
            ObsScenes.Add(scene);
        }

        if (ObsScenes.Count > 0 && string.IsNullOrWhiteSpace(SelectedObsScene))
        {
            SelectedObsScene = ObsScenes[0];
        }

        AddLog($"OBS scenes loaded: {ObsScenes.Count}");
    }

    private async Task SwitchObsSceneAsync()
    {
        var status = await _obsWebSocketService.SwitchSceneAsync(SelectedObsScene);
        ObsStatus = status;
        AddLog(status);
        await RefreshObsSourcesAsync();
    }

    private async Task RefreshObsSourcesAsync()
    {
        var sources = await _obsWebSocketService.GetSourcesInSceneAsync(SelectedObsScene);
        ObsSources.Clear();
        foreach (var source in sources)
        {
            ObsSources.Add(source);
        }

        if (ObsSources.Count > 0 && string.IsNullOrWhiteSpace(SelectedObsSource))
        {
            SelectedObsSource = ObsSources[0];
        }

        AddLog($"OBS sources loaded: {ObsSources.Count}");
    }

    private async Task ToggleOverlaySourceAsync(bool visible)
    {
        if (visible && !_overlayHttpServer.IsRunning)
        {
            StartOverlayServer();
        }

        // Sync UI connection state with actual websocket state.
        IsObsConnected = _obsWebSocketService.IsConnected;
        if (!IsObsConnected && visible)
        {
            if (!int.TryParse(ObsPort, out var reconnectPort) || reconnectPort <= 0)
            {
                reconnectPort = 4455;
            }

            var reconnectStatus = await _obsWebSocketService.ConnectAsync(ObsHost, reconnectPort, ObsPassword);
            AddLog($"OBS auto-reconnect before SHOW: {reconnectStatus}");
            IsObsConnected = _obsWebSocketService.IsConnected || reconnectStatus.Contains("connected successfully", StringComparison.OrdinalIgnoreCase);
            ObsStatus = reconnectStatus;
        }

        if (visible && string.IsNullOrWhiteSpace(SelectedObsScene))
        {
            var liveProgramScene = await _obsWebSocketService.GetCurrentProgramSceneAsync();
            if (!string.IsNullOrWhiteSpace(liveProgramScene))
            {
                SelectedObsScene = liveProgramScene;
            }
        }

        if (visible && !string.IsNullOrWhiteSpace(SelectedObsScene) && ObsSources.Count == 0)
        {
            await RefreshObsSourcesAsync();
        }

        // Robust source targeting for real-world OBS setups:
        // users often rename Browser source (e.g. "Browser"), while app default is "Titan AI Overlay".
        var candidateNames = new List<string>();
        void AddCandidate(string? name)
        {
            if (!string.IsNullOrWhiteSpace(name) &&
                !candidateNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                candidateNames.Add(name);
            }
        }

        AddCandidate(ObsOverlaySourceName);
        AddCandidate(SelectedObsSource);
        foreach (var source in ObsSources)
        {
            AddCandidate(source);
        }
        AddCandidate("Browser");
        AddCandidate("Titan AI Overlay");

        string status = "Please select scene and source.";
        foreach (var candidate in candidateNames)
        {
            status = await _obsWebSocketService.SetSourceVisibilityAsync(SelectedObsScene, candidate, visible);
            if (status.StartsWith("Source shown", StringComparison.OrdinalIgnoreCase) ||
                status.StartsWith("Source hidden", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(ObsOverlaySourceName, candidate, StringComparison.Ordinal))
                {
                    ObsOverlaySourceName = candidate;
                    AddLog($"Overlay source auto-mapped to: {candidate}");
                }

                break;
            }
        }

        if (status.StartsWith("Source shown", StringComparison.OrdinalIgnoreCase))
        {
            SetOverlayVisibleInProgram(true);
        }
        else if (status.StartsWith("Source hidden", StringComparison.OrdinalIgnoreCase))
        {
            SetOverlayVisibleInProgram(false);
        }
        else if (status.Contains("OBS not connected", StringComparison.OrdinalIgnoreCase))
        {
            IsObsConnected = false;
            SetOverlayVisibleInProgram(false);
        }
        else if (visible)
        {
            var diagnostic = BuildOverlayShowDiagnostic(status, candidateNames);
            AddLog(diagnostic, "WARN");
            MessageBox.Show(
                diagnostic,
                "SHOW OVERLAY FAILED",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        ObsStatus = status;
        AddLog(status);
        RefreshLiveSetupWizardState();
    }

    private string BuildOverlayShowDiagnostic(string status, IReadOnlyList<string> candidateNames)
    {
        var scene = string.IsNullOrWhiteSpace(SelectedObsScene) ? "(chưa chọn scene)" : SelectedObsScene;
        var source = string.IsNullOrWhiteSpace(ObsOverlaySourceName) ? "(trống)" : ObsOverlaySourceName;
        var candidates = candidateNames.Count == 0
            ? "(không có)"
            : string.Join(", ", candidateNames);

        return
            "Không thể SHOW overlay lên OBS.\n\n" +
            $"Chi tiết: {status}\n" +
            $"OBS Connected: {(IsObsConnected ? "Yes" : "No")}\n" +
            $"Scene đang chọn: {scene}\n" +
            $"Overlay source hiện tại: {source}\n" +
            $"Danh sách source đã thử: {candidates}\n\n" +
            "Cách xử lý nhanh:\n" +
            "1) Vào OBS, đảm bảo Browser Source đang nằm trong scene đang phát Program.\n" +
            "2) Đặt tên source đúng với ô OVERLAY SOURCE NAME (hoặc đặt là 'Browser').\n" +
            "3) Bấm CONNECT OBS lại rồi bấm SHOW.";
    }

    private async Task PushReplyToObsTextAsync()
    {
        var status = await _obsWebSocketService.UpdateTextSourceAsync(ObsTextSourceName, CurrentReply);
        ObsStatus = status;
        AddLog(status);
    }

    private async Task SelectChatRegionAsync()
    {
        if (_chatRegionSelector is null)
        {
            AddLog("Region selector is unavailable.", "WARN");
            return;
        }

        var region = await _chatRegionSelector();
        if (region is null)
        {
            AddLog("Chat region selection cancelled.", "WARN");
            return;
        }

        _selectedChatRegion = region;
        RaisePropertyChanged(nameof(IsSelectChatRegionSetupCompleted));
        OcrStatus = $"Region selected: {(int)region.Value.Width}x{(int)region.Value.Height}";
        AddLog("Chat region selected for OCR.");
        RefreshLiveSetupWizardState();
    }

    private void StartOcrCapture()
    {
        CheckOcrSetup();
        if (!OcrSetupStatus.Equals("OCR tiếng Việt sẵn sàng", StringComparison.Ordinal))
        {
            OcrStatus = OcrSetupStatus;
            AddLog(OcrSetupStatus, "WARN");
            MessageBox.Show(
                "OCR tiếng Việt chưa sẵn sàng.\n\nVào OBS/Titan setup để thêm file vie.traineddata vào thư mục tessdata, rồi bấm START OCR lại.",
                "OCR Setup Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_selectedChatRegion is null)
        {
            AddLog("Select chat region before starting OCR.", "WARN");
            OcrStatus = "OCR needs chat region.";
            MessageBox.Show(
                "Anh cần bấm SELECT CHAT REGION trước khi START OCR.",
                "OCR Region Required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (int.TryParse(OcrCooldownSeconds, out var cooldown))
        {
            _ocrChatCaptureService.CooldownSeconds = Math.Clamp(cooldown, 1, 30);
        }

        _ocrChatCaptureService.BlacklistKeywords.Clear();
        foreach (var item in BlacklistInput.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            _ocrChatCaptureService.BlacklistKeywords.Add(item);
        }

        _ocrChatCaptureService.Start(_selectedChatRegion.Value);
        IsOcrRunning = true;
        AddLog("OCR live capture started.");
    }

    private void StopOcrCapture()
    {
        _ocrChatCaptureService.Stop();
        IsOcrRunning = false;
        OcrStatus = "OCR stopped.";
        AddLog("OCR live capture stopped.");
    }

    private void CheckOcrSetup()
    {
        var (isReady, status, folder) = _ocrChatCaptureService.CheckSetup();
        OcrSetupStatus = status;
        OcrStatus = status;
        if (isReady)
        {
            AddLog($"OCR Ready ({folder})");
        }
        else
        {
            AddLog(status, "WARN");
        }
    }

    private void OpenTessdataFolder()
    {
        try
        {
            var openedFolder = _ocrChatCaptureService.OpenTessdataFolder();
            AddLog($"Opened tessdata folder: {openedFolder}");
        }
        catch (Exception ex)
        {
            AddLog($"Failed to open tessdata folder: {ex.Message}", "ERROR");
        }
    }

    private async Task ProcessDetectedCommentAsync(DetectedChatMessage message)
    {
        var duplicateKey = $"{message.UserName}|{message.CommentText}".ToLowerInvariant().Trim();
        if (_recentSpokenCommentKeys.TryGetValue(duplicateKey, out var seenAt) &&
            DateTime.Now - seenAt < TimeSpan.FromSeconds(20))
        {
            AddLog("DUPLICATE COMMENT IGNORED", "WARN");
            return;
        }

        _recentSpokenCommentKeys[duplicateKey] = DateTime.Now;
        if (_recentSpokenCommentKeys.Count > 300)
        {
            var expiredKeys = _recentSpokenCommentKeys
                .Where(p => DateTime.Now - p.Value > TimeSpan.FromMinutes(5))
                .Select(p => p.Key)
                .ToList();
            foreach (var key in expiredKeys)
            {
                _recentSpokenCommentKeys.Remove(key);
            }
        }

        LastDetectedComment = $"{message.UserName}: {message.CommentText}";

        AddLog($"OCR RAW: {message.RawText}");

        var catalogMatch = _aiReplyService.EvaluateCatalogMatch(message.CommentText);
        AddLog($"NORMALIZED SKU BLOB: {catalogMatch.NormalizedBlob}");
        var effectiveConfidence = message.ConfidenceScore;
        if (catalogMatch.HasMatch)
        {
            AddLog($"MATCHED PRODUCT: {catalogMatch.ProductName}" +
                   (catalogMatch.ExactPrimaryKey ? " (exact normalizedKey)" : " (unique SKU substring)"));
            effectiveConfidence = Math.Max(effectiveConfidence, 85);
        }

        AddLog($"FINAL CONFIDENCE: {effectiveConfidence}");

        _commentCount++;
        var liveComment = new LiveComment
        {
            UserName = message.UserName,
            CommentText = message.CommentText,
            ConfidenceScore = effectiveConfidence,
            Timestamp = DateTime.Now
        };

        Comments.Insert(0, liveComment);
        PrepareCommentInboundSpotlight(Comments, liveComment);
        LatestCommentPreview = $"{liveComment.UserName}: {liveComment.CommentText}";
        _lastViewerCommentForPersonality = liveComment.CommentText;
        ApplyLiveAiState(LiveAiState.Listening);
        await Task.Yield();
        AddLog($"OCR CONFIDENCE (sensor): {message.ConfidenceScore}");

        var confidenceThreshold = 40;
        if (int.TryParse(OcrConfidenceThreshold, out var parsedThreshold))
        {
            confidenceThreshold = Math.Clamp(parsedThreshold, 0, 100);
        }

        if (effectiveConfidence < confidenceThreshold)
        {
            PendingApproval = new PendingApprovalItem
            {
                UserName = liveComment.UserName,
                OriginalComment = liveComment.CommentText,
                AiReply = "Low confidence OCR comment. Review manually before generating/speaking.",
                ReplyType = "LOW_CONFIDENCE",
                VoicePreset = SelectedVoiceStylePreset,
                ConfidenceScore = effectiveConfidence
            };
            TriggerPendingAlert();
            AddLog("LOW CONFIDENCE -> PENDING", "WARN");
            AddLog($"OCR captured comment from {liveComment.UserName}.");
            return;
        }

        if (AutoReplyEnabled && IsAiEngineEnabled)
        {
            PushReplyGeneration();
            AiReplyResult? replyResult = null;
            try
            {
                replyResult = await _aiReplyService.GenerateReplyAsync(OpenAiApiKey, liveComment);
            }
            finally
            {
                FinishReplyGeneration(liveComment.CommentText);
            }

            if (replyResult is not null)
            {
                _lastReplyType = replyResult.ReplyType;
                ApplyAutoVoicePreset(_lastReplyType);
                CurrentReply = replyResult.ReplyText;
                MarkCommentAiHandled(liveComment);

                if (IsManualApproveMode)
                {
                    QueuePendingApproval(liveComment, replyResult);
                }
            }
        }

        _overlayHttpServer.UpdateData(liveComment, CurrentReply);

        if (!IsManualApproveMode && AutoReplyEnabled && AutoTtsEnabled)
        {
            if (_ttsDelayMs > 0)
            {
                await Task.Delay(_ttsDelayMs);
            }

            if (!IsMuted && !string.IsNullOrWhiteSpace(CurrentReply))
            {
                IsAiSpeaking = true;
                TtsStatus = "Speaking";
            }

            var speechText = PrepareReplyForSpeechForViewer(CurrentReply, liveComment.UserName);
            var warning = await _textToSpeechService.SpeakAsync(speechText);
            IsAiSpeaking = false;
            TtsStatus = IsMuted ? "Muted" : "Idle";
            if (!string.IsNullOrWhiteSpace(warning))
            {
                AddLog(warning, "WARN");
            }
        }

        if (!IsManualApproveMode)
        {
            await TrySyncObsTextAsync(CurrentReply);
        }

        if (!IsManualApproveMode &&
            AutoReplyEnabled &&
            AutoSpeakLiveReply &&
            IsAiEngineEnabled &&
            !string.IsNullOrWhiteSpace(CurrentReply))
        {
            await EnqueueLiveSpeakAsync(liveComment, _lastReplyType, CurrentReply, effectiveConfidence >= confidenceThreshold);
        }

        AddLog($"OCR captured comment from {liveComment.UserName}.");
    }

    private async Task TrySyncObsTextAsync(string reply)
    {
        if (!_obsWebSocketService.IsConnected)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ObsTextSourceName))
        {
            return;
        }

        var status = await _obsWebSocketService.UpdateTextSourceAsync(ObsTextSourceName, reply);
        ObsStatus = status;
    }

    private void AddLog(string message, string severity = "INFO")
    {
        Logs.Insert(0, new LogEntry
        {
            Timestamp = DateTime.Now,
            Severity = severity,
            Message = message
        });
    }

    private void UpdateRealtimeUi()
    {
        if (_currentStartupPhase != StartupPhase.Running)
        {
            return;
        }

        _uiPulseTick++;
        var denseVisualTick = (_uiPulseTick & 1) == 0;

        IsLivePulsing = !IsLivePulsing;
        CpuUsage = Math.Clamp(CpuUsage + (_random.NextDouble() * 8 - 4), 10, 92);
        RamUsage = Math.Clamp(RamUsage + (_random.NextDouble() * 6 - 3), 18, 88);

        var sourceLeft = 12 + _random.NextDouble() * (IsAiSpeaking ? 86 : 42);
        var sourceRight = 12 + _random.NextDouble() * (IsAiSpeaking ? 84 : 40);
        PeakLeft = MoveWithBallistics(PeakLeft, sourceLeft, attack: 0.36, decay: 0.11);
        PeakRight = MoveWithBallistics(PeakRight, sourceRight, attack: 0.34, decay: 0.10);
        RmsLevel = MoveWithBallistics(RmsLevel, (PeakLeft + PeakRight) * 0.48, attack: 0.16, decay: 0.08);
        PeakHoldLeft = PeakLeft > PeakHoldLeft ? PeakLeft : Math.Max(PeakLeft, PeakHoldLeft - 0.9);
        PeakHoldRight = PeakRight > PeakHoldRight ? PeakRight : Math.Max(PeakRight, PeakHoldRight - 0.9);

        if (denseVisualTick)
        {
            for (var i = 0; i < WaveformBars.Count; i++)
            {
                WaveformBars[i] = 6 + _random.NextDouble() * (IsAiSpeaking ? 54 : 24);
            }

            if (_random.NextDouble() > 0.88)
            {
                PlaceholderComments.Insert(0, $"viewer{_random.Next(600, 999)}: {GetPlaceholderQuestion()}");
                while (PlaceholderComments.Count > 6)
                {
                    PlaceholderComments.RemoveAt(PlaceholderComments.Count - 1);
                }
            }

            for (var i = 0; i < LiveSpeakingWaveBars.Count; i++)
            {
                LiveSpeakingWaveBars[i] =
                    _currentLiveAiState == LiveAiState.Speaking && IsLiveAiSpeakingStatus(TtsStatus)
                        ? 8 + _random.NextDouble() * 42
                        : 6 + _random.NextDouble() * 6;
            }

            var studioSpeaking = IsLiveAiSpeakingStatus(TtsStatus);
            var studioEnergyTarget = studioSpeaking ? 28 + _random.NextDouble() * 72 : 5 + _random.NextDouble() * 12;
            LiveStudioAudioEnergy = Math.Clamp(
                MoveWithBallistics(
                    LiveStudioAudioEnergy,
                    studioEnergyTarget,
                    attack: studioSpeaking ? 0.32 : 0.16,
                    decay: studioSpeaking ? 0.08 : 0.26),
                0,
                100);
        }

        TryDecayLiveAiToIdle();

        if (!IsAiSpeaking)
        {
            TtsStatus = IsMuted ? "Muted" : "Idle";
            RefreshLiveFooterMetersOnly();
            return;
        }

        if (_random.NextDouble() > 0.84)
        {
            IsAiSpeaking = false;
            TtsStatus = IsMuted ? "Muted" : "Idle";
        }

        RefreshLiveFooterMetersOnly();
    }

    private static double MoveWithBallistics(double current, double target, double attack, double decay)
    {
        var factor = target >= current ? attack : decay;
        return current + (target - current) * factor;
    }

    private string GetPlaceholderQuestion()
    {
        var pool = new[]
        {
            "Ben minh co bao hanh bao lau?",
            "VF5 danh ngoai troi on khong?",
            "Cho xin combo day du phu kien.",
            "Co ho tro lap dat tan noi khong?",
            "Cho xin gia livestream uu dai hom nay."
        };
        return pool[_random.Next(pool.Length)];
    }

    private void ApplyPerformanceProfile()
    {
        _textToSpeechService.SetLowLatencyMode(IsLowLatencyMode);

        if (IsLowLatencyMode)
        {
            _ocrChatCaptureService.CaptureIntervalMs = 450;
            _aiReplyService.RequestTimeoutSeconds = 8;
            _ttsDelayMs = 40;
            AddLog("LOW LATENCY enabled: OCR 450ms, AI timeout 8s, TTS delay 40ms.");
            return;
        }

        _ocrChatCaptureService.CaptureIntervalMs = 900;
        _aiReplyService.RequestTimeoutSeconds = 20;
        _ttsDelayMs = 220;
        AddLog("LOW LATENCY disabled: default performance profile restored.");
    }

    private void ValidateLiveReady()
    {
        var checks = new List<(string Name, bool Ok)>
        {
            ("OBS connected", IsObsConnected),
            ("Overlay running", _overlayHttpServer.IsRunning),
            ("OCR ready", OcrSetupStatus.Equals("OCR tiếng Việt sẵn sàng", StringComparison.Ordinal)),
            ("TTS ready", _textToSpeechService.IsReady),
            ("Product DB loaded", _aiReplyService.ProductCatalogLoaded)
        };

        var allGood = checks.All(c => c.Ok);
        IsLiveReady = allGood;

        if (allGood)
        {
            MessageBox.Show("SYSTEM READY FOR LIVE", "Titan AI Live", MessageBoxButton.OK, MessageBoxImage.Information);
            AddLog("LIVE READY validation passed.");
            return;
        }

        var failed = string.Join(", ", checks.Where(c => !c.Ok).Select(c => c.Name));
        MessageBox.Show($"System chưa sẵn sàng.\nThiếu: {failed}", "Titan AI Live", MessageBoxButton.OK, MessageBoxImage.Warning);
        AddLog($"LIVE READY validation failed: {failed}", "WARN");
    }

    private void ToggleDspToolsPanel()
    {
        MessageBox.Show("DSP tools panel placeholder (version 1).", "TITAN DSP ENGINE", MessageBoxButton.OK, MessageBoxImage.Information);
        AddLog(IsDspEngineEnabled ? "TITAN DSP ENGINE enabled." : "TITAN DSP ENGINE disabled.");
    }

    private void ApplyVoiceSettings()
    {
        _textToSpeechService.ConfigureVoice(SelectedVoiceName, VoiceSpeed, VoicePitch);
    }

    private void ApplyPendingVoiceSelection()
    {
        if (!CanApplyPendingVoiceSelection)
        {
            return;
        }

        SelectedVoiceName = PendingVoiceName;
        AddLog($"VOICE APPLIED: {SelectedVoiceName}");
    }

    private void ApplyAutoVoicePreset(AiReplyType replyType)
    {
        if (!AutoVoiceStylePreset)
        {
            return;
        }

        var preset = replyType switch
        {
            AiReplyType.ProductPrice => "Price Quote",
            AiReplyType.TechnicalRedirect => "Technical Redirect",
            AiReplyType.FallbackContact => "Closing Sale",
            AiReplyType.UnclearOcr => "Urgent / Warning",
            AiReplyType.ScriptedFaq => "Closing Sale",
            _ => "Greeting"
        };

        _isApplyingAutoVoicePreset = true;
        try
        {
            SelectedVoiceStylePreset = preset;
        }
        finally
        {
            _isApplyingAutoVoicePreset = false;
        }
    }

    private void ApplyVoiceStylePreset(string preset, bool fromAutoSelection)
    {
        var mapped = preset switch
        {
            "Greeting" => (1.00, 1.06),
            "Price Quote" => (1.10, 1.00),
            "Technical Redirect" => (0.90, 0.94),
            "Closing Sale" => (1.00, 1.03),
            "Urgent / Warning" => (0.86, 0.90),
            _ => (1.00, 1.00)
        };

        VoiceSpeed = mapped.Item1;
        VoicePitch = mapped.Item2;
    }

    private void RefreshVoicePresetStatus(bool logChange)
    {
        var mode = AutoVoiceStylePreset ? "AUTO" : "MANUAL";
        var presetUpper = SelectedVoiceStylePreset.ToUpperInvariant();
        CurrentVoicePresetStatusText = $"CURRENT PRESET: {presetUpper} ({mode})";
        CurrentVoicePresetBadgeBrush = BuildPresetBadgeBrush(SelectedVoiceStylePreset);

        if (logChange)
        {
            AddLog($"VOICE PRESET -> {presetUpper} ({mode})");
        }
    }

    private static Brush BuildPresetBadgeBrush(string preset)
    {
        var hex = preset switch
        {
            "Greeting" => "#5CE1E6",
            "Price Quote" => "#F5C542",
            "Technical Redirect" => "#FF9A3C",
            "Closing Sale" => "#63D47A",
            "Urgent / Warning" => "#FF5A5A",
            _ => "#8DA8CC"
        };

        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    private void QueuePendingApproval(LiveComment comment, AiReplyResult replyResult)
    {
        PendingApproval = new PendingApprovalItem
        {
            UserName = comment.UserName,
            OriginalComment = comment.CommentText,
            AiReply = replyResult.ReplyText,
            ReplyType = ToReplyTypeLabel(replyResult.ReplyType),
            VoicePreset = SelectedVoiceStylePreset,
            ConfidenceScore = comment.ConfidenceScore
        };
        TriggerPendingAlert();
        AddLog("APPROVAL REQUIRED");
        AddLog("PENDING ALERT");
    }

    private async Task ApproveAndSpeakAsync()
    {
        if (PendingApproval is null)
        {
            return;
        }

        CurrentReply = PendingApproval.AiReply;
        if (!IsMuted && !string.IsNullOrWhiteSpace(CurrentReply))
        {
            IsAiSpeaking = true;
            TtsStatus = "Speaking";
        }

        var speechText = PrepareReplyForSpeechForViewer(CurrentReply, PendingApproval.UserName);
        var warning = await _textToSpeechService.SpeakAsync(speechText);
        IsAiSpeaking = false;
        TtsStatus = IsMuted ? "Muted" : "Idle";
        if (!string.IsNullOrWhiteSpace(warning))
        {
            AddLog(warning, "WARN");
        }

        PendingApproval = null;
        AddLog("APPROVED SPEAK");
    }

    private async Task ApproveAndPushObsAsync()
    {
        if (PendingApproval is null)
        {
            return;
        }

        CurrentReply = PendingApproval.AiReply;
        await TrySyncObsTextAsync(CurrentReply);
        PendingApproval = null;
        AddLog("APPROVED OBS");
    }

    private void IgnorePendingApproval()
    {
        if (PendingApproval is null)
        {
            return;
        }

        PendingApproval = null;
        AddLog("IGNORED");
    }

    private void RaiseApprovalCommandsCanExecuteChanged()
    {
        ApproveAndSpeakCommand.RaiseCanExecuteChanged();
        ApproveAndPushObsCommand.RaiseCanExecuteChanged();
        IgnorePendingCommand.RaiseCanExecuteChanged();
    }

    private static string ToReplyTypeLabel(AiReplyType replyType)
    {
        return replyType switch
        {
            AiReplyType.ProductPrice => "PRODUCT_PRICE",
            AiReplyType.TechnicalRedirect => "TECHNICAL_REDIRECT",
            AiReplyType.FallbackContact => "FALLBACK_CONTACT",
            AiReplyType.UnclearOcr => "UNCLEAR_OCR",
            AiReplyType.ScriptedFaq => "SCRIPTED_FAQ",
            _ => "GREETING"
        };
    }

    private void TriggerPendingAlert()
    {
        if (BeepOnPending)
        {
            SystemSounds.Beep.Play();
        }

        _pendingAlertCts?.Cancel();
        _pendingAlertCts?.Dispose();
        _pendingAlertCts = new CancellationTokenSource();
        var token = _pendingAlertCts.Token;
        _ = RunPendingAlertAsync(token);
    }

    private async Task RunPendingAlertAsync(CancellationToken token)
    {
        try
        {
            IsPendingAlertActive = true;
            await Task.Delay(2000, token);
            IsPendingAlertActive = false;
        }
        catch (OperationCanceledException)
        {
            // no-op
        }
    }

    private async Task EnqueueLiveSpeakAsync(LiveComment liveComment, AiReplyType replyType, string replyText, bool highConfidence)
    {
        if (!IsSafeForTts(replyText, highConfidence))
        {
            AddLog("LOW CONFIDENCE -> PENDING", "WARN");
            PendingApproval = new PendingApprovalItem
            {
                UserName = liveComment.UserName,
                OriginalComment = liveComment.CommentText,
                AiReply = replyText,
                ReplyType = ToReplyTypeLabel(replyType),
                VoicePreset = SelectedVoiceStylePreset,
                ConfidenceScore = liveComment.ConfidenceScore
            };
            TriggerPendingAlert();
            return;
        }

        if (_speakQueue.Count >= 3)
        {
            AddLog("QUEUE FULL", "WARN");
            return;
        }

        if (highConfidence && IsAiSpeaking)
        {
            _currentSpeakCts?.Cancel();
            _textToSpeechService.Interrupt();
            IsAiSpeaking = false;
            TtsStatus = IsMuted ? "Muted" : "Idle";
            AddLog("VOICE INTERRUPTED");
        }

        _speakQueue.Enqueue(new SpeakQueueItem(liveComment, replyType, replyText));
        if (!_isProcessingSpeakQueue)
        {
            _ = ProcessSpeakQueueAsync();
        }

        await Task.CompletedTask;
    }

    private async Task ProcessSpeakQueueAsync()
    {
        if (_isProcessingSpeakQueue)
        {
            return;
        }

        _isProcessingSpeakQueue = true;
        try
        {
            while (_speakQueue.Count > 0)
            {
                var item = _speakQueue.Dequeue();
                var elapsed = DateTime.Now - _lastSpokenReplyAt;
                if (elapsed < TimeSpan.FromSeconds(2))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2) - elapsed);
                }

                _currentSpeakCts?.Dispose();
                _currentSpeakCts = new CancellationTokenSource();
                var token = _currentSpeakCts.Token;

                IsAiSpeaking = true;
                TtsStatus = IsMuted ? "Muted" : "Speaking";
                BackgroundMediaVolume = 35;
                AddLog("SPEAKING LIVE REPLY");

                var speakText = PrepareReplyForSpeechForViewer(item.ReplyText, item.Comment.UserName);
                var warning = await _textToSpeechService.SpeakAsync(speakText);
                if (!string.IsNullOrWhiteSpace(warning))
                {
                    AddLog(warning, "WARN");
                    if (warning.StartsWith("EDGE TTS FAILED:", StringComparison.Ordinal))
                    {
                        AddLog("EDGE TTS FAILED -> PENDING", "WARN");
                        PendingApproval = new PendingApprovalItem
                        {
                            UserName = item.Comment.UserName,
                            OriginalComment = item.Comment.CommentText,
                            AiReply = item.ReplyText,
                            ReplyType = "EDGE_TTS_FAILED",
                            VoicePreset = SelectedVoiceStylePreset,
                            ConfidenceScore = item.Comment.ConfidenceScore
                        };
                        TriggerPendingAlert();
                    }
                }

                try
                {
                    await Task.Delay(EstimateSpeakDuration(speakText), token);
                }
                catch (OperationCanceledException)
                {
                    // interrupted by new high-confidence comment
                }

                BackgroundMediaVolume = 100;
                if (!token.IsCancellationRequested)
                {
                    _lastSpokenReplyAt = DateTime.Now;
                }

                IsAiSpeaking = false;
                TtsStatus = IsMuted ? "Muted" : "Idle";
            }
        }
        finally
        {
            _isProcessingSpeakQueue = false;
        }
    }

    private static TimeSpan EstimateSpeakDuration(string text)
    {
        var words = Math.Max(1, text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        var seconds = Math.Clamp(words * 0.35, 2.0, 10.0);
        return TimeSpan.FromSeconds(seconds);
    }

    private static string PrepareReplyForSpeech(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        if (trimmed.Length <= 220)
        {
            return trimmed;
        }

        var sentenceSplit = trimmed.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (sentenceSplit.Length > 0 && sentenceSplit[0].Length >= 24)
        {
            var firstSentence = sentenceSplit[0];
            return firstSentence.Length <= 220 ? firstSentence : firstSentence[..220];
        }

        return trimmed[..220];
    }

    private static string PrepareReplyForSpeechForViewer(string? replyText, string? viewerName)
    {
        var baseSpeech = PrepareReplyForSpeech(replyText);
        if (string.IsNullOrWhiteSpace(baseSpeech))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(viewerName))
        {
            return baseSpeech;
        }

        var name = viewerName.Trim();
        if (name.Length > 24)
        {
            name = name[..24];
        }

        var withName = $"Anh {name}, {baseSpeech}";
        return withName.Length <= 240 ? withName : withName[..240];
    }

    private static bool IsSafeForTts(string text, bool highConfidence)
    {
        if (!highConfidence)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lowered = text.ToLowerInvariant();
        if (Regex.IsMatch(lowered, @"\b(obs|facebook|dashboard|entry_point|localhost|http|www|\.com|tieu chuan cong dong)\b"))
        {
            return false;
        }

        var words = lowered.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length < 2)
        {
            return false;
        }

        var symbolCount = text.Count(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && c is not '.' and not ',' and not '?' and not '!');
        if (symbolCount / (double)Math.Max(1, text.Length) > 0.25)
        {
            return false;
        }

        if (!Regex.IsMatch(lowered, "[aeiouyăâêôơưáàảãạắằẳẵặấầẩẫậéèẻẽẹếềểễệíìỉĩịóòỏõọốồổỗộớờởỡợúùủũụứừửữựýỳỷỹỵ]"))
        {
            return false;
        }

        return true;
    }

    private void UpdateTtsEngineBadge(TextToSpeechService.TtsEngineState state, string selectedVoice, string? lastError)
    {
        _ttsRouteEngineState = state;
        RaisePropertyChanged(nameof(LiveFooterVoiceRouteCaption));
        RaisePropertyChanged(nameof(LiveFooterVoiceRouteBrush));

        switch (state)
        {
            case TextToSpeechService.TtsEngineState.EdgeVietnamese:
                TtsEngineBadgeText = "TTS ENGINE: EDGE VIETNAMESE";
                TtsEngineBadgeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5CE1E6"));
                TtsErrorMessage = string.Empty;
                TtsErrorVisibility = Visibility.Collapsed;
                AddLog("TTS ENGINE -> EDGE VIETNAMESE");
                break;
            case TextToSpeechService.TtsEngineState.WebVietnamese:
                TtsEngineBadgeText = "TTS ENGINE: WEB VIETNAMESE";
                TtsEngineBadgeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#63D47A"));
                TtsErrorMessage = string.Empty;
                TtsErrorVisibility = Visibility.Collapsed;
                AddLog("TTS ENGINE -> WEB VIETNAMESE");
                break;
            default:
                TtsEngineBadgeText = "TTS ENGINE: EDGE ERROR";
                TtsEngineBadgeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5A5A"));
                TtsErrorMessage = string.IsNullOrWhiteSpace(lastError)
                    ? "TTS lỗi chưa rõ nguyên nhân."
                    : $"TTS LỖI: {lastError}";
                TtsErrorVisibility = Visibility.Visible;
                AddLog("TTS ENGINE -> ERROR", "WARN");
                break;
        }

        var lastErrorText = string.IsNullOrWhiteSpace(lastError) ? "None" : lastError;
        TtsEngineTooltip = $"Voice: {selectedVoice}\nLast Error: {lastErrorText}";
    }

    private static readonly string[] VoiceStylePresets =
    [
        "Greeting",
        "Price Quote",
        "Technical Redirect",
        "Closing Sale",
        "Urgent / Warning"
    ];

    private async Task PreviewVoiceAsync()
    {
        var warning = await _textToSpeechService.SpeakAsync("Xin chào anh/chị, em là trợ lý giọng nói Titan.", preview: true);
        if (!string.IsNullOrWhiteSpace(warning))
        {
            AddLog(warning, "WARN");
        }
        AddLog($"Voice preview: {SelectedVoiceName}");
    }

    private async Task TestEdgeVietnameseAsync()
    {
        SelectedVoiceName = "vi-VN-HoaiMyNeural";
        var warning = await _textToSpeechService.SpeakAsync("Xin chào, đây là kiểm tra Edge tiếng Việt.");
        if (!string.IsNullOrWhiteSpace(warning))
        {
            AddLog("EDGE TTS FAILED -> PENDING", "WARN");
            PendingApproval = new PendingApprovalItem
            {
                UserName = "system",
                OriginalComment = "Test Edge Vietnamese",
                AiReply = warning,
                ReplyType = "EDGE_TTS_FAILED",
                VoicePreset = SelectedVoiceStylePreset,
                ConfidenceScore = 100
            };
            TriggerPendingAlert();
        }
    }

    private sealed record SpeakQueueItem(LiveComment Comment, AiReplyType ReplyType, string ReplyText);
}
