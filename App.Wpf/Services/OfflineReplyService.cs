using TitanAILivePC.Models;

namespace TitanAILivePC.Services;

public sealed class OfflineReplyService
{
    public string GenerateReply(LiveComment? comment)
    {
        return TitanKnowledgeBase.BuildNoInfoFallbackReply();
    }
}
