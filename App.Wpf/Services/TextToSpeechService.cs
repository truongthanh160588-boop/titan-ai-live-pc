using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Media;
using System.IO;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http;
using EdgeTTS;

namespace TitanAILivePC.Services;

public sealed class TextToSpeechService : IDisposable
{
    private const string DefaultEdgeVoice = "vi-VN-HoaiMyNeural";
    /// <remarks><see cref="SpeechSynthesizer"/> ctor can block several seconds on first access — lazy-init after UI shows.</remarks>
    private SpeechSynthesizer? _fallbackSynthesizer;
    private static readonly HttpClient HttpClient = new();
    private MediaPlayer? _edgePlayer;
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _speakCts;
    private bool _isMuted;
    private bool _isLowLatencyMode;
    private string _voiceName = "vi-VN-HoaiMyNeural";
    private double _voiceSpeed = 1.0;
    private double _voicePitch = 1.0;
    private string? _lastAudioFilePath;

    public IReadOnlyList<string> VietnameseVoices { get; } =
    [
        "vi-VN-HoaiMyNeural",
        "vi-VN-NamMinhNeural",
        "vi-VN-MaiAnhNeural",
        "vi-VN-ThanhNeural",
        "en-US-AriaNeural",
        "en-US-JennyNeural",
        "en-US-GuyNeural",
        "en-GB-SoniaNeural",
        "en-GB-RyanNeural",
        "ja-JP-NanamiNeural",
        "ko-KR-SunHiNeural"
    ];

    public bool IsReady => true;
    public event Action<string>? DebugLog;
    public event Action<TtsEngineState, string, string?>? EngineStateChanged;

    private SpeechSynthesizer FallbackSynth =>
        _fallbackSynthesizer ??= new SpeechSynthesizer();

    /// <summary>WPF <see cref="MediaPlayer"/> — created on first playback/stop (typically UI thread).</summary>
    private MediaPlayer EdgePlayer =>
        _edgePlayer ??= new MediaPlayer();

    public string GetInstalledVoicesDebugText()
    {
        var installed = FallbackSynth.GetInstalledVoices()
            .Select(v => $"{v.VoiceInfo.Name} [{v.VoiceInfo.Culture}]")
            .ToList();
        return installed.Count == 0
            ? "TTS VOICES: No System.Speech voices detected."
            : $"TTS VOICES: {string.Join(" | ", installed)}";
    }

    public void SetMuted(bool isMuted)
    {
        _isMuted = isMuted;
        if (isMuted)
        {
            StopCurrentSpeech();
        }
    }

    public void SetLowLatencyMode(bool isLowLatencyMode)
    {
        _isLowLatencyMode = isLowLatencyMode;
    }

    public void ConfigureVoice(string voiceName, double speed, double pitch)
    {
        _voiceName = VietnameseVoices.Contains(voiceName, StringComparer.OrdinalIgnoreCase) ? voiceName : DefaultEdgeVoice;
        _voiceSpeed = Math.Clamp(speed, 0.5, 1.8);
        _voicePitch = Math.Clamp(pitch, 0.5, 1.8);
    }

    public async Task<string?> SpeakAsync(string text, bool preview = false)
    {
        if (_isMuted || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        StopCurrentSpeech();
        var speechText = preview ? "Xin chào anh chị, Titan AI sẵn sàng livestream." : text;
        DebugLog?.Invoke($"TTS INPUT: {SanitizeForLog(text)}");
        speechText = SanitizeTextForTts(speechText);
        DebugLog?.Invoke($"TTS SANITIZED: {SanitizeForLog(speechText)}");
        if (string.IsNullOrWhiteSpace(speechText))
        {
            return "Không có nội dung hợp lệ để đọc TTS.";
        }

        var cts = new CancellationTokenSource();
        lock (_syncRoot)
        {
            _speakCts = cts;
        }

        var voiceToUse = ResolveVietnameseEdgeVoice();
        DebugLog?.Invoke($"EDGE TTS USING: {voiceToUse}");
        PublishEngineState(TtsEngineState.EdgeVietnamese, voiceToUse, null);

        var edgeError = await TrySpeakWithEdgeAsync(voiceToUse, speechText, cts.Token);
        if (edgeError is null)
        {
            PublishEngineState(TtsEngineState.EdgeVietnamese, voiceToUse, null);
            return null;
        }

        if (edgeError.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            // Live pipeline interruptions are expected; do not treat as engine failure.
            return null;
        }

        DebugLog?.Invoke($"EDGE TTS FAILED: {edgeError}");
        DebugLog?.Invoke("EDGE TTS RETRY 1");
        var retryError = await TrySpeakWithEdgeAsync(voiceToUse, speechText, cts.Token);
        if (retryError is null)
        {
            PublishEngineState(TtsEngineState.EdgeVietnamese, voiceToUse, null);
            return null;
        }

        if (retryError.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        DebugLog?.Invoke($"EDGE TTS FAILED: {retryError}");
        DebugLog?.Invoke("EDGE TTS RETRY 2");
        var retryError2 = await TrySpeakWithEdgeAsync(voiceToUse, speechText, cts.Token);
        if (retryError2 is null)
        {
            PublishEngineState(TtsEngineState.EdgeVietnamese, voiceToUse, null);
            return null;
        }

        if (retryError2.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        DebugLog?.Invoke($"EDGE TTS FAILED: {retryError2}");
        var webFallbackError = await TrySpeakWithWebVietnameseAsync(speechText, cts.Token);
        if (webFallbackError is null)
        {
            PublishEngineState(TtsEngineState.WebVietnamese, "vi-VN Web TTS", null);
            return null;
        }

        PublishEngineState(TtsEngineState.Error, voiceToUse, $"{retryError2}; Web fallback: {webFallbackError}");
        return $"EDGE TTS FAILED: {retryError2}";
    }

    private async Task<string?> TrySpeakWithEdgeAsync(string voiceToUse, string speechText, CancellationToken token)
    {
        try
        {
            var audioPath = Path.Combine(Path.GetTempPath(), $"titan-edge-{Guid.NewGuid():N}.mp3");
            var communicate = new Communicate(
                speechText,
                voiceToUse,
                ToEdgePercent(_voiceSpeed),
                "+0%",
                ToEdgePitch(_voicePitch),
                null);

            await communicate.Save(audioPath, token);
            if (token.IsCancellationRequested)
            {
                return "Cancelled";
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                EdgePlayer.Open(new Uri(audioPath));
                EdgePlayer.Play();
            });

            CleanupLastAudioFile(audioPath);
            return null;
        }
        catch (OperationCanceledException)
        {
            return "Cancelled";
        }
        catch (Exception ex)
        {
            DebugLog?.Invoke($"TTS EXCEPTION: {ex.Message}");
            DebugLog?.Invoke($"STACKTRACE: {ex.StackTrace}");
            return ex.Message;
        }
    }

    public void Interrupt()
    {
        StopCurrentSpeech();
    }

    private void StopCurrentSpeech()
    {
        lock (_syncRoot)
        {
            _speakCts?.Cancel();
            _speakCts?.Dispose();
            _speakCts = null;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            _edgePlayer?.Stop();
            return;
        }

        dispatcher.Invoke(() => _edgePlayer?.Stop(), System.Windows.Threading.DispatcherPriority.Normal);
        _fallbackSynthesizer?.SpeakAsyncCancelAll();
    }

    private void CleanupLastAudioFile(string currentAudioFilePath)
    {
        var oldPath = _lastAudioFilePath;
        _lastAudioFilePath = currentAudioFilePath;

        if (string.IsNullOrWhiteSpace(oldPath) || oldPath.Equals(currentAudioFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            File.Delete(oldPath);
        }
        catch
        {
            // ignore temp cleanup failures
        }
    }

    private string ToEdgePercent(double scalar)
    {
        var factor = _isLowLatencyMode ? scalar * 1.05 : scalar;
        var percent = (factor - 1.0) * 100.0;
        return $"{percent:+0;-0;0}%";
    }

    private static string ToEdgePitch(double scalar)
    {
        // Edge TTS expects pitch as Hz (e.g. +0Hz), not percentage.
        var hz = (scalar - 1.0) * 50.0;
        var rounded = (int)Math.Round(hz, MidpointRounding.AwayFromZero);
        return rounded >= 0 ? $"+{rounded}Hz" : $"{rounded}Hz";
    }

    private static int ConvertToFallbackRate(double speed)
    {
        return (int)Math.Round((speed - 1.0) * 10.0);
    }

    private string ResolveVietnameseEdgeVoice()
    {
        if (VietnameseVoices.Contains(_voiceName, StringComparer.OrdinalIgnoreCase))
        {
            return _voiceName;
        }

        return DefaultEdgeVoice;
    }

    // Windows fallback intentionally disabled for live Vietnamese mode (Edge-only).

    private static readonly string[] ViDigitWords =
        ["không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín"];

    private static string SanitizeTextForTts(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Normalize first to reduce weird OCR unicode variants.
        text = text.Normalize(NormalizationForm.FormKC);

        // Remove control chars and invalid unicode categories.
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsControl(c) && c is not '\r' and not '\n' and not '\t')
            {
                continue;
            }

            if (char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.OtherNotAssigned)
            {
                continue;
            }

            if (char.GetUnicodeCategory(c) == UnicodeCategory.Surrogate)
            {
                continue;
            }

            sb.Append(c);
        }

        var sanitized = sb.ToString();
        sanitized = Regex.Replace(sanitized, @"https?://\S+|www\.\S+", " ", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"\b(obs|facebook|live producer|dashboard|entry_point|localhost|www|\.com|titan ai live system|tieu chuan cong dong)\b", " ", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"[^\p{L}\p{N}\s\.\,\!\?\:\;\-]", " ");
        sanitized = Regex.Replace(sanitized, @"([!?.:\-_,;])\1{1,}", "$1");

        // Multi-line price bullets → clearer spoken pacing for neural/Web TTS.
        sanitized = Regex.Replace(sanitized, @"[\r\n]+\s*-\s*", ". Tiếp theo, ", RegexOptions.None);
        sanitized = Regex.Replace(sanitized, @"[\r\n]+", ". ", RegexOptions.None);

        // Vietnamese money → words first; then natural spacing for SKUs (avoid “trừ”, wrong dots).
        sanitized = ExpandVndPricesForSpeech(sanitized);
        sanitized = ExpandProductModelsForNaturalSpeech(sanitized);
        sanitized = NormalizeHardwareCodesForSpeech(sanitized);
        sanitized = sanitized.Replace(" / ", " trên ", StringComparison.Ordinal);

        sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
        var maxChars = Regex.IsMatch(sanitized, @"một trong các mẫu|tiếp theo,", RegexOptions.IgnoreCase)
            ? 560
            : 240;
        if (sanitized.Length > maxChars)
        {
            sanitized = sanitized[..maxChars].Trim();
        }

        // Final XML-safe cleanup for Edge TTS payload.
        sanitized = sanitized.Replace("&", " và ");
        sanitized = Regex.Replace(sanitized, @"[<>""']", " ");
        sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();

        return sanitized;
    }

    /// <summary>
    /// Turns dotted VND amounts like 22.000.000đ into spoken Vietnamese so neural/Web TTS does not garble "." groups.
    /// </summary>
    private static string ExpandVndPricesForSpeech(string text)
    {
        return Regex.Replace(
            text,
            @"\d{1,3}(?:\.\d{3})+(?:\s*[đd](?:ồng)?)?",
            m =>
            {
                var digitsOnly = Regex.Replace(m.Value, @"[^\d]", "");
                if (!long.TryParse(digitsOnly, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) ||
                    amount <= 0)
                {
                    return m.Value;
                }

                return SpeakVnd(amount);
            },
            RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Đọc mã như “SU118-F15”, “T-4.16Pro”, “VF5”, “T-60” với khoảng trắng rõ ràng.
    /// </summary>
    private static string ExpandProductModelsForNaturalSpeech(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var ti = CultureInfo.InvariantCulture.TextInfo;

        text = Regex.Replace(text, @"\bSU\s*(\d+)\s*[-]?\s*([A-Za-z]{1,4}\d?)\b",
            m => $"SU {m.Groups[1].Value} {m.Groups[2].Value}",
            RegexOptions.IgnoreCase);

        text = Regex.Replace(text, @"\bVF\s*(\d)\b", "VF $1", RegexOptions.IgnoreCase);

        // OCR thường dính “T416PRO” không dấu chấm — tách thành “T 4 16 Pro”.
        text = Regex.Replace(text, @"\bT(\d)(\d{2})(Pro|PLUS|SUB|FULL|Lite)\b",
            m => $"T {m.Groups[1].Value} {m.Groups[2].Value} {ti.ToTitleCase(m.Groups[3].Value.ToLowerInvariant())}",
            RegexOptions.IgnoreCase);

        text = Regex.Replace(text,
            @"\bT[- ]?(\d)[.\s]+(\d+)\s*(Pro|PLUS|SUB|FULL|Lite)?\b",
            m =>
            {
                var suf = m.Groups[3].Success && m.Groups[3].Length > 0
                    ? " " + ti.ToTitleCase(m.Groups[3].Value.ToLowerInvariant())
                    : "";
                return $"T {m.Groups[1].Value} {m.Groups[2].Value}{suf}";
            },
            RegexOptions.IgnoreCase);

        text = Regex.Replace(text,
            @"\bF\s*(\d+)\s*[-]?\s*[Vv]\s*(\d+)(Lite|Pro)?\b",
            m =>
            {
                var suf = m.Groups[3].Success && m.Groups[3].Length > 0
                    ? " " + ti.ToTitleCase(m.Groups[3].Value.ToLowerInvariant())
                    : "";
                return $"F {m.Groups[1].Value} V {m.Groups[2].Value}{suf}";
            },
            RegexOptions.IgnoreCase);

        text = Regex.Replace(text, @"\bTFX\s*(\d+)\b", "TFX $1", RegexOptions.IgnoreCase);

        // T-60, T208 — 2–4 chữ số, không bị nhầm với “T 4 16 …” đã tách ở trên.
        text = Regex.Replace(text, @"\bT[- ]?(\d{2,4})\b(?!\s*\d)",
            "T $1",
            RegexOptions.IgnoreCase);

        return text;
    }

    /// <summary>
    /// Tokens SKU lạ — gạch/ngăn chữ-số còn sót sau bước trên.
    /// </summary>
    private static string NormalizeHardwareCodesForSpeech(string text)
    {
        text = Regex.Replace(text, @"(?<=[A-Za-z0-9])-(?=[A-Za-z0-9])", " ");
        text = Regex.Replace(text, @"(?<=\d)(?=\p{L})", " ");
        text = Regex.Replace(text, @"(?<=\p{L})(?=\d)", " ");
        return text;
    }

    private static string SpeakVnd(long amount)
    {
        if (amount <= 0)
        {
            return string.Empty;
        }

        string[] scales = ["", " nghìn", " triệu", " tỷ"];
        var segments = new List<string>();
        var idx = 0;
        var remaining = amount;
        while (remaining > 0 && idx < scales.Length)
        {
            var chunk = (int)(remaining % 1000);
            remaining /= 1000;
            if (chunk != 0)
            {
                segments.Insert(0, ReadTripleVi(chunk) + scales[idx]);
            }

            idx++;
        }

        var spoken = string.Join(" ", segments.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        return string.IsNullOrEmpty(spoken) ? string.Empty : spoken + " đồng";
    }

    /// <summary>Reads 1–999 for Vietnamese money grouping.</summary>
    private static string ReadTripleVi(int n)
    {
        if (n <= 0 || n > 999)
        {
            return string.Empty;
        }

        var hundred = n / 100;
        var remainder = n % 100;
        var ten = remainder / 10;
        var one = remainder % 10;
        var parts = new List<string>();

        if (hundred > 0)
        {
            parts.Add($"{ViDigitWords[hundred]} trăm");
        }

        if (ten > 0)
        {
            if (ten == 1)
            {
                if (one == 0)
                {
                    parts.Add("mười");
                }
                else if (one == 5)
                {
                    parts.Add("mười lăm");
                }
                else
                {
                    parts.Add($"mười {ViDigitWords[one]}");
                }
            }
            else
            {
                var chunk = $"{ViDigitWords[ten]} mươi";
                if (one == 1)
                {
                    chunk += " mốt";
                }
                else if (one == 5)
                {
                    chunk += " lăm";
                }
                else if (one != 0)
                {
                    chunk += $" {ViDigitWords[one]}";
                }

                parts.Add(chunk);
            }
        }
        else if (one > 0)
        {
            if (hundred > 0)
            {
                parts.Add("linh");
                parts.Add(ViDigitWords[one]);
            }
            else
            {
                parts.Add(ViDigitWords[one]);
            }
        }

        return string.Join(" ", parts).Trim();
    }

    private async Task<string?> TrySpeakWithWebVietnameseAsync(string speechText, CancellationToken token)
    {
        try
        {
            DebugLog?.Invoke("WEB VIETNAMESE TTS FALLBACK");
            var audioPath = Path.Combine(Path.GetTempPath(), $"titan-web-vi-{Guid.NewGuid():N}.mp3");
            var url =
                $"https://translate.google.com/translate_tts?ie=UTF-8&client=tw-ob&tl=vi&q={Uri.EscapeDataString(speechText)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            using var response = await HttpClient.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
            {
                return $"Web Vietnamese TTS HTTP {(int)response.StatusCode}";
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(token);
            await File.WriteAllBytesAsync(audioPath, bytes, token);
            if (token.IsCancellationRequested)
            {
                return "Cancelled";
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                EdgePlayer.Open(new Uri(audioPath));
                EdgePlayer.Play();
            });

            CleanupLastAudioFile(audioPath);
            return null;
        }
        catch (Exception ex)
        {
            DebugLog?.Invoke($"TTS EXCEPTION: {ex.Message}");
            DebugLog?.Invoke($"STACKTRACE: {ex.StackTrace}");
            return ex.Message;
        }
    }

    private static string SanitizeForLog(string text)
    {
        var flat = Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();
        return flat.Length <= 220 ? flat : $"{flat[..220]}...";
    }

    private void PublishEngineState(TtsEngineState state, string selectedVoice, string? lastError)
    {
        EngineStateChanged?.Invoke(state, selectedVoice, lastError);
    }

    public enum TtsEngineState
    {
        EdgeVietnamese,
        WebVietnamese,
        Error
    }

    public void Dispose()
    {
        StopCurrentSpeech();

        if (!string.IsNullOrWhiteSpace(_lastAudioFilePath))
        {
            try
            {
                File.Delete(_lastAudioFilePath);
            }
            catch
            {
                // ignore temp cleanup failures
            }
        }

        _edgePlayer?.Close();
        _fallbackSynthesizer?.Dispose();
    }
}
