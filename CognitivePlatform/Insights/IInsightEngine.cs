using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Api.Insights;

public interface IInsightEngine
{
    Task<IReadOnlyList<Insight>> GenerateInsightsAsync( ConversationContext context
                                                      , CancellationToken   ct = default );
}
