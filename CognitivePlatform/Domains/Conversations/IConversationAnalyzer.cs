namespace CognitivePlatform.Api.Domains.Conversations;

/// <summary>
/// Analyzes a completed conversation and produces structured understanding.
/// Input: the full conversation aggregate (record + transcript + participants).
/// Output: a <see cref="ConversationAnalysis"/> containing summaries, topics,
/// questions, decisions, action items, and important statements with provenance.
/// </summary>
public interface IConversationAnalyzer
{
    Task<ConversationAnalysis> AnalyzeAsync( ConversationDetails details
                                           , CancellationToken cancellationToken = default );
}
