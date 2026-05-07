namespace TitanAILivePC.Models;

public enum AiReplyType
{
    Greeting,
    ProductPrice,
    TechnicalRedirect,
    FallbackContact,
    UnclearOcr,
    /// <summary>Kịch bản FAQ livestream (combo, T-60, đồng bộ, v.v.).</summary>
    ScriptedFaq
}

public sealed class AiReplyResult
{
    public string ReplyText { get; init; } = string.Empty;
    public AiReplyType ReplyType { get; init; } = AiReplyType.FallbackContact;
}
