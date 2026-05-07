using System.Globalization;
using System.Text;

namespace TitanAILivePC.Services;

public static class TitanKnowledgeBase
{
    public const string AssistantDisplayName = "Chào Anh Chị";
    public const string Brand = "Titan Audio Vietnam";
    public const string Website = "www.titanaudio.vn";
    public const string Product = "TITAN VF5";
    public const string Slogan = "Am thanh sieu sach - Cong suat cuc manh - Do ben tuyet doi.";
    public const string Driver = "VF5 uses B&C Italy driver.";
    public const string Focus = "Titan focuses on professional clean sound, strong power, and high durability.";
    public const string Contact = "Contact/Zalo: 0974 70 4444.";
    public const string CompanyHotline = "0967 839 446";
    public const string TechnicalHotline = "0974 70 4444";
    public const string TechnicalName = "Trương Thanh";
    private static readonly string[] TechnicalKeywords =
    [
        "dsp","crossover","delay","phase","chong hu","feedback","setup","line array","tuning","canh chinh",
        "eq","fir","cardioid","sub delay","phase alignment","do loa","analyzer"
    ];

    public static string BuildPrompt(string viewerComment)
    {
        return $"""
                You are Titan AI Live sales assistant for Titan Audio Vietnam.
                Always answer in friendly Vietnamese for livestream selling.
                Every answer must start with: "{AssistantDisplayName}:"
                If product info is not found or uncertain, return exactly this fallback:
                "{BuildNoInfoFallbackReply().Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n")}"
                Keep answers concise and practical.

                Knowledge:
                - Brand: {Brand}
                - Website: {Website}
                - Product: {Product}
                - Slogan: {Slogan}
                - {Driver}
                - {Focus}
                - {Contact}

                Viewer comment:
                {viewerComment}
                """;
    }

    public static string BuildNoInfoFallbackReply()
    {
        return $"""
                {AssistantDisplayName}:
                Anh/chị vui lòng liên hệ Hotline Titan Đồng Nai {CompanyHotline}
                hoặc kỹ thuật Titan Đồng Nai qua số {TechnicalHotline} gặp {TechnicalName} để được tư vấn chi tiết nhé.
                """;
    }

    public static string BuildUnclearOcrReply()
    {
        return $"""
                {AssistantDisplayName}:
                Anh/chị có thể nhắn lại rõ hơn giúp em để Titan hỗ trợ chính xác hơn nhé.
                """;
    }

    public static string BuildTechnicalFallbackReply()
    {
        return $"""
                {AssistantDisplayName}:
                Dạ với các vấn đề kỹ thuật chuyên sâu, setup hệ thống hoặc phối ghép thiết bị, anh/chị vui lòng liên hệ kỹ thuật Titan Đồng Nai: {TechnicalHotline} gặp {TechnicalName} để được hỗ trợ chi tiết nhé.
                """;
    }

    public static bool IsTechnicalDeepQuestion(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return false;
        }

        var normalized = NormalizeViComment(question);
        return TechnicalKeywords.Any(k => normalized.Contains(k, StringComparison.Ordinal));
    }

    public static string PrefixAssistantName(string body)
    {
        var prefix = $"{AssistantDisplayName}:";
        if (string.IsNullOrWhiteSpace(body))
        {
            return BuildNoInfoFallbackReply();
        }

        var trimmed = body.Trim();
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{prefix} {trimmed}";
    }

    /// <summary>Chuẩn hóa comment để so khớp intent (không dùng cho SKU catalog).</summary>
    public static string NormalizeViComment(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lowered = text.ToLowerInvariant().Replace('-', ' ').Replace('_', ' ');
        var normalized = lowered.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) ? c : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

