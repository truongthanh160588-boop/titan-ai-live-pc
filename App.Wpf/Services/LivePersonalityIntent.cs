using TitanAILivePC.Models;

namespace TitanAILivePC.Services;

/// <summary>Lightweight viewer-comment cues for Live Mode personality only (does not affect AI reply logic).</summary>
public static class LivePersonalityIntent
{
    private static readonly string[] ClosingTokens =
    [
        "chot", "lay hang", " lay ", "lay ", " ship ", "ship", "dat hang", " dat ", "mua hang", " mua ", "mua ", "inbox",
        "sdt", "hotline", "zalo",
    ];

    private static readonly string[] HotlineTokens =
    [
        "combo", "300 khach", "san khau", "tu van", "ky thuat", "cau hinh", "ho tro", "tro giup",
    ];

    public static LiveAiState ClassifyFromViewerComment(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return LiveAiState.Idle;
        }

        if (TitanKnowledgeBase.IsTechnicalDeepQuestion(raw))
        {
            return LiveAiState.TechnicalMode;
        }

        var n = TitanKnowledgeBase.NormalizeViComment(raw);
        if (ContainsClosingIntent(n))
        {
            return LiveAiState.ClosingSale;
        }

        return ContainsHotlineIntent(n) ? LiveAiState.HotlineMode : LiveAiState.Idle;
    }

    private static bool ContainsClosingIntent(string normalized)
    {
        return ClosingTokens.Any(t => normalized.Contains(t, StringComparison.Ordinal));
    }

    private static bool ContainsHotlineIntent(string normalized)
    {
        return HotlineTokens.Any(t => normalized.Contains(t, StringComparison.Ordinal));
    }
}
