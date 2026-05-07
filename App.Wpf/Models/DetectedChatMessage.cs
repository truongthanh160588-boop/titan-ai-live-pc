namespace TitanAILivePC.Models;

public sealed class DetectedChatMessage
{
    public string UserName { get; init; } = string.Empty;
    public string CommentText { get; init; } = string.Empty;
    public string RawText { get; init; } = string.Empty;
    public int ConfidenceScore { get; init; }
}
