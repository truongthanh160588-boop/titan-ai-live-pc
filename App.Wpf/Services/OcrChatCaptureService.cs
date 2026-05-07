using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Tesseract;
using TitanAILivePC.Models;
using Rect = System.Windows.Rect;

namespace TitanAILivePC.Services;

public sealed class OcrChatCaptureService : IDisposable
{
    private const string OcrDataFileName = "vie.traineddata";
    private static readonly string[] ProductHints =
    [
        "vf5",
        "vfs",
        "vf4",
        "vf4pro",
        "f712",
        "400b",
        "118 x",
        "118 ml",
        "118 f15",
        "118 xl",
        "su118",
        "t416",
        "t416pro",
        "t60",
        "t 60",
        "t208",
        "tfx16",
        "t46",
        "t218",
        "t212",
        "t226"
    ];

    /// <summary>OCR often reads VF5 as VFS on screen fonts; normalize before parsing/confidence.</summary>
    private static readonly Regex VfsLooksLikeVf5Regex = new(@"\bVF\s*S\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] BuyingHints =
    [
        "gia", "bao nhieu", "nhieu tien", "con hang", "ship", "mua", "tu van"
    ];

    private static readonly string[] ReactionHints =
    [
        "haha", "ha ha", "tim", "trai tim", "yeu thich", "thuong thuong", "bay to cam xuc", "da tha"
    ];

    private static readonly string[] NoiseTerms =
    [
        "viewer",
        "binh luan",
        "tra loi",
        "thich",
        "ghim",
        "tieu chuan cong dong",
        "viet binh luan",
        "titan ai live system",
        "obs",
        "disconnected",
        "facebook",
        "live producer",
        "http",
        "www",
        ".com",
        "dashboard",
        "entry_point",
        "localhost"
    ];

    private static readonly Regex UrlLikeRegex = new(@"(https?://|www\.|\.com\b|localhost|entry_point|dashboard|facebook|live producer)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly HashSet<string> _dedupe = [];
    private CancellationTokenSource? _cts;
    private DateTime _lastEmit = DateTime.MinValue;
    private TesseractEngine? _engine;

    public event Action<DetectedChatMessage>? MessageDetected;
    public event Action<string>? StatusChanged;
    public event Action<string>? DebugLog;

    public bool IsRunning => _cts is not null;
    public string LastDetectedRaw { get; private set; } = "No OCR result yet.";
    public int CooldownSeconds { get; set; } = 3;
    public int CaptureIntervalMs { get; set; } = 900;
    public List<string> BlacklistKeywords { get; } = [];
    public string PreferredTessdataFolder => Path.Combine(AppContext.BaseDirectory, "tessdata");

    public void Start(Rect regionInPixels)
    {
        if (IsRunning)
        {
            return;
        }

        var (ready, status, _) = CheckSetup();
        if (!ready)
        {
            StatusChanged?.Invoke(status);
            return;
        }

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(regionInPixels, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private async Task LoopAsync(Rect regionInPixels, CancellationToken cancellationToken)
    {
        StatusChanged?.Invoke("OCR starting...");
        if (!TryInitEngine())
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var bitmap = CaptureRegion(regionInPixels);
                var text = RunOcr(bitmap);
                LastDetectedRaw = text;
                DebugLog?.Invoke($"RAW OCR: {SanitizeForLog(text)}");

                foreach (var message in ParseMessages(text))
                {
                    if (ShouldSkip(message))
                    {
                        continue;
                    }

                    _lastEmit = DateTime.Now;
                    MessageDetected?.Invoke(message);
                }

                StatusChanged?.Invoke($"OCR active ({DateTime.Now:HH:mm:ss})");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"OCR warning: {ex.Message}");
            }

            await Task.Delay(CaptureIntervalMs, cancellationToken);
        }

        StatusChanged?.Invoke("OCR stopped.");
    }

    public (bool IsReady, string Status, string? TessdataFolder) CheckSetup()
    {
        var candidates = GetTessdataCandidates();
        foreach (var folder in candidates)
        {
            var trainedData = Path.Combine(folder, OcrDataFileName);
            if (File.Exists(trainedData))
            {
                return (true, "OCR tiếng Việt sẵn sàng", folder);
            }
        }

        return (false, "Thiếu dữ liệu OCR tiếng Việt: vie.traineddata", null);
    }

    public string OpenTessdataFolder()
    {
        Directory.CreateDirectory(PreferredTessdataFolder);
        Process.Start(new ProcessStartInfo
        {
            FileName = PreferredTessdataFolder,
            UseShellExecute = true
        });
        return PreferredTessdataFolder;
    }

    private bool TryInitEngine()
    {
        if (_engine is not null)
        {
            return true;
        }

        var (ready, status, tessdataFolder) = CheckSetup();
        if (!ready || string.IsNullOrWhiteSpace(tessdataFolder))
        {
            StatusChanged?.Invoke(status);
            return false;
        }

        try
        {
            _engine = new TesseractEngine(tessdataFolder, "vie");
            StatusChanged?.Invoke("OCR engine ready (Tesseract).");
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"OCR warning: failed to initialize engine ({ex.Message})");
            return false;
        }
    }

    private string RunOcr(Bitmap bitmap)
    {
        if (_engine is null)
        {
            return string.Empty;
        }

        using var memory = new MemoryStream();
        bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
        using var pix = Pix.LoadFromMemory(memory.ToArray());
        using var page = _engine.Process(pix);
        return page.GetText() ?? string.Empty;
    }

    private static Bitmap CaptureRegion(Rect region)
    {
        var width = Math.Max(1, (int)region.Width);
        var height = Math.Max(1, (int)region.Height);
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen((int)region.X, (int)region.Y, 0, 0, new Size(width, height));
        return bitmap;
    }

    private IEnumerable<DetectedChatMessage> ParseMessages(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            DebugLog?.Invoke("REJECTED REASON: OCR empty.");
            yield break;
        }

        var rawLines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidateLines = new List<string>();
        foreach (var line in rawLines)
        {
            var cleaned = Regex.Replace(line, @"\s+", " ").Trim();
            if (!TryAcceptCandidateLine(cleaned, out var rejectedReason))
            {
                DebugLog?.Invoke($"REJECTED REASON: {rejectedReason} | \"{cleaned}\"");
                continue;
            }

            candidateLines.Add(cleaned);
        }

        for (var i = 0; i < candidateLines.Count; i++)
        {
            var current = candidateLines[i];
            var split = current.Split(':', 2, StringSplitOptions.TrimEntries);
            if (split.Length == 2 && IsLikelyUsername(split[0]))
            {
                var usernameCandidate = split[0];
                var commentCandidate = split[1];
                if (ShouldSwapUsernameAndComment(usernameCandidate, commentCandidate))
                {
                    (usernameCandidate, commentCandidate) = (commentCandidate, usernameCandidate);
                    DebugLog?.Invoke($"OCR SWAP FIX (colon format): username/comment swapped -> {usernameCandidate} | {commentCandidate}");
                }

                var correctedComment = ApplyTitanSkuOcrCorrections(commentCandidate);
                if (!string.Equals(split[1], correctedComment, StringComparison.OrdinalIgnoreCase))
                {
                    DebugLog?.Invoke($"SKU OCR FIX: \"{SanitizeForLog(split[1])}\" → \"{SanitizeForLog(correctedComment)}\"");
                }

                DebugLog?.Invoke($"NORMALIZED SKU BLOB: {ProductKeyNormalizer.NormalizeProductKey(correctedComment)}");

                if (!IsLikelyComment(split[1]) && !IsLikelyComment(correctedComment))
                {
                    DebugLog?.Invoke($"REJECTED REASON: Comment invalid (even after SKU OCR fix) | \"{split[1]}\"");
                    continue;
                }

                var confidence = ComputeConfidence(split[0], correctedComment, usernameMessageFormatDetected: true);
                var msg = new DetectedChatMessage
                {
                    UserName = usernameCandidate,
                    CommentText = correctedComment,
                    RawText = $"{usernameCandidate}: {correctedComment}",
                    ConfidenceScore = confidence
                };
                DebugLog?.Invoke($"FILTERED COMMENT: {msg.UserName} | {msg.CommentText}");
                yield return msg;
                continue;
            }

            if (i + 1 >= candidateLines.Count)
            {
                DebugLog?.Invoke($"REJECTED REASON: Missing comment pair for username/comment format | \"{current}\"");
                continue;
            }

            var username = candidateLines[i];
            var comment = candidateLines[i + 1];
            if (ShouldSwapUsernameAndComment(username, comment))
            {
                (username, comment) = (comment, username);
                DebugLog?.Invoke($"OCR SWAP FIX (pair format): username/comment swapped -> {username} | {comment}");
            }

            if (!IsLikelyUsername(username))
            {
                DebugLog?.Invoke($"REJECTED REASON: Username line invalid | \"{username}\"");
                continue;
            }

            var pairedCorrectedComment = ApplyTitanSkuOcrCorrections(comment);
            if (!string.Equals(comment, pairedCorrectedComment, StringComparison.OrdinalIgnoreCase))
            {
                DebugLog?.Invoke($"SKU OCR FIX: \"{SanitizeForLog(comment)}\" → \"{SanitizeForLog(pairedCorrectedComment)}\"");
            }

            DebugLog?.Invoke($"NORMALIZED SKU BLOB: {ProductKeyNormalizer.NormalizeProductKey(pairedCorrectedComment)}");

            if (!IsLikelyComment(comment) && !IsLikelyComment(pairedCorrectedComment))
            {
                DebugLog?.Invoke($"REJECTED REASON: Comment line invalid | \"{comment}\"");
                continue;
            }

            var message = new DetectedChatMessage
            {
                UserName = username,
                CommentText = pairedCorrectedComment,
                RawText = $"{username} | {pairedCorrectedComment}",
                ConfidenceScore = ComputeConfidence(username, pairedCorrectedComment, usernameMessageFormatDetected: true)
            };
            DebugLog?.Invoke($"FILTERED COMMENT: {message.UserName} | {message.CommentText}");
            yield return message;
            i++;
        }
    }

    private bool ShouldSkip(DetectedChatMessage message)
    {
        var normalized = $"{message.UserName}:{message.CommentText}".ToLowerInvariant();
        if (_dedupe.Contains(normalized))
        {
            return true;
        }

        if (DateTime.Now - _lastEmit < TimeSpan.FromSeconds(Math.Max(1, CooldownSeconds)))
        {
            return true;
        }

        if (BlacklistKeywords.Any(k => !string.IsNullOrWhiteSpace(k) &&
            message.CommentText.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        _dedupe.Add(normalized);
        if (_dedupe.Count > 400)
        {
            _dedupe.Clear();
        }

        return false;
    }

    public void Dispose()
    {
        Stop();
        _engine?.Dispose();
    }

    private static IEnumerable<string> GetTessdataCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "tessdata");

        var projectDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        yield return Path.Combine(projectDirectory, "tessdata");
    }

    private static bool TryAcceptCandidateLine(string line, out string reason)
    {
        if (line.Length < 3)
        {
            reason = "Line too short (<3).";
            return false;
        }

        if (line.Length > 80)
        {
            reason = "Line too long (>80).";
            return false;
        }

        if (UrlLikeRegex.IsMatch(line))
        {
            reason = "Contains URL/browser/tab text.";
            return false;
        }

        var normalized = Normalize(line);
        if (NoiseTerms.Any(term => normalized.Contains(term, StringComparison.Ordinal)))
        {
            reason = "Contains known Facebook/UI noise.";
            return false;
        }

        var symbolCount = line.Count(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && c is not '-' and not ':' and not '?');
        var symbolRatio = symbolCount / (double)Math.Max(1, line.Length);
        if (symbolRatio > 0.35)
        {
            reason = "Too many symbols.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsLikelyUsername(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length is < 2 or > 32)
        {
            return false;
        }

        if (line.Contains(':'))
        {
            return false;
        }

        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 5)
        {
            return false;
        }

        var normalized = Normalize(line);
        if (ProductHints.Any(h => normalized.Contains(h, StringComparison.Ordinal)))
        {
            return false;
        }

        return words.All(w => w.All(c => char.IsLetter(c) || c == '.' || c == '_'));
    }

    private static bool IsLikelyComment(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var normalized = Normalize(line);
        if (NoiseTerms.Any(term => normalized.Contains(term, StringComparison.Ordinal)))
        {
            return false;
        }

        if (line.Length is < 3 or > 80)
        {
            return false;
        }

        if (!line.Any(char.IsLetterOrDigit))
        {
            return false;
        }

        var hasProductHint = ProductHints.Any(h => normalized.Contains(h, StringComparison.Ordinal));
        var hasReactionHint = ReactionHints.Any(h => normalized.Contains(h, StringComparison.Ordinal));
        var looksQuestion = normalized.Contains("?", StringComparison.Ordinal) ||
                            normalized.Contains("khong", StringComparison.Ordinal) ||
                            normalized.Contains("sao", StringComparison.Ordinal) ||
                            normalized.Contains("bao", StringComparison.Ordinal);

        return hasProductHint ||
               hasReactionHint ||
               looksQuestion ||
               line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2;
    }

    private static bool ShouldSwapUsernameAndComment(string usernameCandidate, string commentCandidate)
    {
        var u = Normalize(usernameCandidate);
        var c = Normalize(commentCandidate);

        var usernameLooksGreetingToken =
            u is "chao" or "xin chao" or "alo" or "hello" or "hi" or "haha" or "ha ha" or "tim";

        if (!usernameLooksGreetingToken)
        {
            return false;
        }

        // If comment line looks like a real person name, swap.
        return IsLikelyUsername(commentCandidate);
    }

    /// <summary>
    /// Fixes systematic misreads from Antialiased UI text (VF5 → VFS, VF S, etc.).
    /// Titan livestream domain: treat as VF5 unless proven otherwise.
    /// </summary>
    private static string ApplyTitanSkuOcrCorrections(string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return comment;
        }

        var t = VfsLooksLikeVf5Regex.Replace(comment.Trim(), "VF5");
        t = Regex.Replace(t, @"\bT416PR0\b", "T416PRO", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\bSU11B\b", "SU118", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\bT6O\b", "T60", RegexOptions.IgnoreCase);
        return t;
    }

    private static string Normalize(string input)
    {
        var lower = input.ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
        var normalized = lower.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c is '.' or ':' or '?' ? c : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string SanitizeForLog(string raw)
    {
        var flattened = Regex.Replace(raw, @"\s+", " ").Trim();
        if (flattened.Length <= 220)
        {
            return flattened;
        }

        return $"{flattened[..220]}...";
    }

    private static int ComputeConfidence(string username, string comment, bool usernameMessageFormatDetected)
    {
        var score = 20;
        var normalized = Normalize(comment);
        var usernameNorm = Normalize(username);

        if (ProductHints.Any(h => normalized.Contains(h, StringComparison.Ordinal)))
        {
            score += 30;
        }

        if (BuyingHints.Any(h => normalized.Contains(h, StringComparison.Ordinal)))
        {
            score += 25;
        }

        if (ContainsVietnameseReadableWords(comment))
        {
            score += 15;
        }

        if (usernameMessageFormatDetected)
        {
            score += 10;
        }

        if (NoiseTerms.Any(t => normalized.Contains(t, StringComparison.Ordinal) || usernameNorm.Contains(t, StringComparison.Ordinal)))
        {
            score -= 30;
        }

        if (UrlLikeRegex.IsMatch(comment) || UrlLikeRegex.IsMatch(username))
        {
            score -= 40;
        }

        if (comment.Length < 3 || comment.Length > 80)
        {
            score -= 20;
        }

        return Math.Clamp(score, 0, 100);
    }

    private static bool ContainsVietnameseReadableWords(string comment)
    {
        var words = comment.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length < 2)
        {
            return false;
        }

        var readableWordCount = words.Count(w => w.Any(char.IsLetter));
        return readableWordCount >= 2;
    }
}
