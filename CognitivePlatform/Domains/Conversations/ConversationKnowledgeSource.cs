using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.KnowledgeInbox;
using CognitivePlatform.Api.KnowledgeInbox.Interfaces;

namespace CognitivePlatform.Api.Domains.Conversations;

/// <summary>
/// Knowledge source for recorded conversations. Adapts conversation records,
/// transcripts, and structured analyses into the unified Knowledge Inbox system.
/// </summary>
public sealed class ConversationKnowledgeSource : IKnowledgeSource
{
    private readonly IObjectStore         _objectStore;
    private readonly IConversationService _conversationService;

    public KnowledgeKind Kind => KnowledgeKind.Conversation;

    public ConversationKnowledgeSource( IObjectStore         objectStore
                                      , IConversationService conversationService )
    {
        _objectStore         = objectStore;
        _conversationService = conversationService;
    }

    public IEnumerable<KnowledgeItemDto> GetKnowledgeItems( KnowledgeQuery    query
                                                           , CancellationToken ct )
    {
        var records = _objectStore.List<ConversationRecord>(partitionKey: null);

        foreach (var record in records)
        {
            if (query.Id is not null && record.Id != query.Id.Value)
            {
                continue;
            }

            var analysis = _objectStore.Get<ConversationAnalysis>($"analysis_{record.Id}", partitionKey: null);
            var transcript = analysis == null
                ? _objectStore.Get<Transcript>($"transcript_{record.Id}", partitionKey: null)
                : null;

            var tags = analysis?.Topics != null && analysis.Topics.Count > 0
                ? analysis.Topics.Select(topic => topic.Content).ToList()
                : new List<string>();

            yield return new KnowledgeItemDto
            {
                Id             = record.Id
              , Kind           = KnowledgeKind.Conversation
              , Title          = record.Title
              , Summary        = DeriveSummary(analysis, transcript)
              , CreatedAt      = record.RecordedAtUtc
              , LastModifiedAt = analysis?.AnalyzedAtUtc ?? record.RecordedAtUtc
              , Status         = record.IsDeleted ? KnowledgeStatus.Deleted : KnowledgeStatus.Active
              , Tags           = tags
              , IsEdited       = false
              , Importance     = null
              , Urgency        = null
            };
        }
    }

    public IReadOnlyList<ObjectHeader> ListHeaders( DateTimeOffset? fromUtc
                                                   , DateTimeOffset? toUtc )
    {
        return _objectStore.List<ConversationRecord>(partitionKey: null, fromUtc: fromUtc, toUtc: toUtc)
                           .Where(record => !record.IsDeleted)
                           .Select(record => new ObjectHeader(
                               record.Id.ToString()
                             , KnowledgeKind.Conversation.ToString()
                             , record.RecordedAtUtc
                             , record.RecordedAtUtc))
                           .ToList();
    }

    public void Archive( Guid              id
                       , CancellationToken ct )
    {
        _conversationService.DeleteRecordingAsync(id, ct).GetAwaiter().GetResult();
    }

    private static string? DeriveSummary( ConversationAnalysis? analysis, Transcript? transcript )
    {
        if (analysis != null && !string.IsNullOrWhiteSpace(analysis.Summary))
        {
            return analysis.Summary.Length <= 180
                ? analysis.Summary
                : string.Concat(analysis.Summary.AsSpan(0, 177), "…");
        }

        if (transcript?.Segments != null && transcript.Segments.Count > 0)
        {
            var preview = transcript.Segments[0].Text;
            return preview.Length <= 140
                ? preview
                : string.Concat(preview.AsSpan(0, 137), "…");
        }

        return null;
    }
}
