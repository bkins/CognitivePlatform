using System;
using System.Collections.Generic;

namespace CognitivePlatform.Api.Domains.Conversations.Copilot;

public sealed class LiveStreamChunkResult
{
    public Guid                       ConversationId       { get; set; }
    public int                        ChunkIndex           { get; set; }
    public TranscriptSegment?         Segment              { get; set; }
    public bool                       IsFinal              { get; set; }
    public List<CopilotInsight>       Insights             { get; set; } = new();
    public bool                       HasActionableInsight => Insights != null && Insights.Count > 0;
    public Dictionary<string, double> SpeakerTalkTime      { get; set; } = new();
    public DateTime                   ProcessedAtUtc       { get; set; } = DateTime.UtcNow;
}
