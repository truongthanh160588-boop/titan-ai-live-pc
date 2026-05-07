namespace TitanAILivePC.Models;

public sealed class PendingApprovalItem
{
    public string UserName { get; init; } = string.Empty;
    public string OriginalComment { get; init; } = string.Empty;
    public string AiReply { get; init; } = string.Empty;
    public string ReplyType { get; init; } = string.Empty;
    public string VoicePreset { get; init; } = string.Empty;
    public int ConfidenceScore { get; init; }
}
