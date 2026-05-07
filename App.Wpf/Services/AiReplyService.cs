using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TitanAILivePC.Models;

namespace TitanAILivePC.Services;

public sealed class AiReplyService
{
    private static readonly HttpClient HttpClient = new();
    private readonly OfflineReplyService _offlineReplyService = new();
    /// <remarks>Defer JSON/catalog parse until first AI path — keeps MainViewModel ctor fast.</remarks>
    private readonly Lazy<ProductCatalogService> _productCatalog = new(static () => new ProductCatalogService());

    private ProductCatalogService Catalog => _productCatalog.Value;

    public int RequestTimeoutSeconds { get; set; } = 20;
    public bool UseShortReplyMode { get; set; } = true;
    public bool ProductCatalogLoaded => Catalog.HasProducts;

    /// <summary>OCR boost / debug: khớp SKU sau NormalizeProductKey.</summary>
    public CatalogMatchInfo EvaluateCatalogMatch(string? commentText) =>
        Catalog.EvaluateCatalogMatch(commentText);

    public async Task<AiReplyResult> GenerateReplyAsync(string apiKey, LiveComment? latestComment, CancellationToken cancellationToken = default)
    {
        if (Catalog.TryBuildPriceReply(latestComment?.CommentText, out var priceReply))
        {
            return new AiReplyResult { ReplyText = priceReply, ReplyType = AiReplyType.ProductPrice };
        }

        if (IsUnknownProductPriceQuery(latestComment?.CommentText, out var normalizedKey))
        {
            return new AiReplyResult
            {
                ReplyText = TitanKnowledgeBase.PrefixAssistantName(
                    $"Em chưa thấy mã {normalizedKey.ToUpperInvariant()} trong bảng giá hiện tại. Anh/chị xác nhận lại đúng mã sản phẩm giúp em để Titan báo giá chính xác ngay ạ."),
                ReplyType = AiReplyType.ScriptedFaq
            };
        }

        if (TitanLivestreamScript.TryMatch(latestComment?.CommentText, out var scriptedBody, out var scriptedType))
        {
            if (scriptedType == AiReplyType.Greeting)
            {
                scriptedBody = PersonalizeGreeting(scriptedBody, latestComment?.UserName);
            }

            return new AiReplyResult
            {
                ReplyText = TitanKnowledgeBase.PrefixAssistantName(scriptedBody),
                ReplyType = scriptedType
            };
        }

        if (TitanKnowledgeBase.IsTechnicalDeepQuestion(latestComment?.CommentText))
        {
            return new AiReplyResult
            {
                ReplyText = TitanKnowledgeBase.BuildTechnicalFallbackReply(),
                ReplyType = AiReplyType.TechnicalRedirect
            };
        }

        if (IsUnclearText(latestComment?.CommentText))
        {
            return new AiReplyResult
            {
                ReplyText = TitanKnowledgeBase.BuildUnclearOcrReply(),
                ReplyType = AiReplyType.UnclearOcr
            };
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiReplyResult
            {
                ReplyText = _offlineReplyService.GenerateReply(latestComment),
                ReplyType = AiReplyType.FallbackContact
            };
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(RequestTimeoutSeconds, 3, 60)));

            var requestObject = new
            {
                model = "gpt-4o-mini",
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = UseShortReplyMode
                            ? "You are a Vietnamese livestream sales assistant specialized in Titan Audio products. Keep replies short (1-2 sentences), direct, and sales-friendly."
                            : "You are a Vietnamese livestream sales assistant specialized in Titan Audio products. Provide detailed, practical sales answers in 3-5 concise sentences."
                    },
                    new { role = "user", content = TitanKnowledgeBase.BuildPrompt(latestComment?.CommentText ?? "Khach chua gui binh luan.") }
                },
                temperature = UseShortReplyMode ? 0.5 : 0.7
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(requestObject), Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(request, linkedCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new AiReplyResult
                {
                    ReplyText = TitanKnowledgeBase.BuildNoInfoFallbackReply(),
                    ReplyType = AiReplyType.FallbackContact
                };
            }

            await using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: linkedCts.Token);
            var content = json.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            var replyText = string.IsNullOrWhiteSpace(content)
                ? TitanKnowledgeBase.BuildNoInfoFallbackReply()
                : TitanKnowledgeBase.PrefixAssistantName(content);

            return new AiReplyResult
            {
                ReplyText = replyText,
                ReplyType = AiReplyType.FallbackContact
            };
        }
        catch
        {
            return new AiReplyResult
            {
                ReplyText = _offlineReplyService.GenerateReply(latestComment),
                ReplyType = AiReplyType.FallbackContact
            };
        }
    }

    private static bool IsUnclearText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var cleaned = Regex.Replace(text, @"\s+", " ").Trim();
        if (cleaned.Length < 4)
        {
            return true;
        }

        var lettersOrDigits = cleaned.Count(char.IsLetterOrDigit);
        if (lettersOrDigits < 3)
        {
            return true;
        }

        var junkRatio = 1 - (lettersOrDigits / (double)Math.Max(1, cleaned.Length));
        return junkRatio > 0.55;
    }

    private static bool IsUnknownProductPriceQuery(string? text, out string normalizedKey)
    {
        normalizedKey = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var n = TitanKnowledgeBase.NormalizeViComment(text);
        var asksPrice = n.Contains("gia", StringComparison.Ordinal) ||
                        n.Contains("bao nhieu", StringComparison.Ordinal) ||
                        n.Contains("bao gia", StringComparison.Ordinal) ||
                        n.Contains("nhieu tien", StringComparison.Ordinal);
        if (!asksPrice)
        {
            return false;
        }

        // Try extract a SKU-like token from raw text first so we don't include trailing words like "gia".
        var compact = Regex.Replace(text, @"\s+", " ").Trim();
        var matches = Regex.Matches(compact, @"\b[a-zA-Z]{1,6}\s*[-]?\s*\d[\w-]*\b");
        if (matches.Count > 0)
        {
            var best = matches
                .Select(m => m.Value.Trim())
                .OrderByDescending(v => v.Length)
                .First();
            normalizedKey = best.Replace(" ", string.Empty, StringComparison.Ordinal);
            return true;
        }

        normalizedKey = ProductKeyNormalizer.NormalizeProductKey(text);
        return Regex.IsMatch(normalizedKey, @"[a-z]+\d", RegexOptions.IgnoreCase);
    }

    private static string PersonalizeGreeting(string scriptedBody, string? userName)
    {
        if (string.IsNullOrWhiteSpace(scriptedBody) || string.IsNullOrWhiteSpace(userName))
        {
            return scriptedBody;
        }

        var cleanedName = userName.Trim();
        if (cleanedName.Length > 32)
        {
            cleanedName = cleanedName[..32];
        }

        if (scriptedBody.StartsWith("Em chào anh.", StringComparison.OrdinalIgnoreCase))
        {
            return scriptedBody.Replace("Em chào anh.", $"Em chào anh {cleanedName}.", StringComparison.OrdinalIgnoreCase);
        }

        return $"Em chào anh {cleanedName}. {scriptedBody}";
    }
}
