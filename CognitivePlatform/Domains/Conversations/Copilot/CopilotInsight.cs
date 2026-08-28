using System;

namespace CognitivePlatform.Api.Domains.Conversations.Copilot;

public class CopilotInsight
{
    public Guid               Id                 { get; set; } = Guid.NewGuid();
    public Guid               ConversationId     { get; set; }
    public DateTime           TimestampUtc       { get; set; } = DateTime.UtcNow;
    public double             AudioOffsetSeconds { get; set; }
    public CopilotInsightType InsightType        { get; set; } = CopilotInsightType.RecallHint;
    public string             Headline           { get; set; } = string.Empty;
    public string             Detail             { get; set; } = string.Empty;
    public float              RelevanceScore     { get; set; } = 1.0f;
    public string             ProvenanceChain    { get; set; } = string.Empty;
    public bool               IsDismissed        { get; set; }
    public bool               IsDeleted          { get; set; }
    public DateTime?          DeletedUtc         { get; set; }
}
