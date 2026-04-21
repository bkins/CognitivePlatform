using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Produces insights for a single domain. Providers run concurrently and must be read-only —
/// no state mutation is permitted.
/// </summary>
public interface IInsightProvider
{
    /// <summary>Domain this provider covers, used for logging and filtering.</summary>
    InsightCategory Category { get; }

    IAsyncEnumerable<Insight> GenerateAsync( ConversationContext context
                                           , IObjectStore        store
                                           , CancellationToken   ct = default );
}
