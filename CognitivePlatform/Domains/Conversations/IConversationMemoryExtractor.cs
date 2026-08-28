using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CognitivePlatform.Api.Domains.Conversations;

/// <summary>
/// Extracts candidate cognitive memories from conversation transcripts and analyses.
/// </summary>
public interface IConversationMemoryExtractor
{
    /// <summary>
    /// Extracts structured candidate memories (facts, commitments, preferences, decisions, plans, context)
    /// from conversation details with segment-level provenance.
    /// </summary>
    Task<List<ConversationMemoryCandidate>> ExtractMemoriesAsync( ConversationDetails details
                                                                , CancellationToken cancellationToken = default );
}
