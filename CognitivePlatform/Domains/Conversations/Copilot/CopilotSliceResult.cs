using System;
using System.Collections.Generic;

namespace CognitivePlatform.Api.Domains.Conversations.Copilot;

public class CopilotSliceResult
{
    public Guid                 ConversationId       { get; set; }
    public int                  SliceIndex           { get; set; }
    public string               TranscribedText      { get; set; } = string.Empty;
    public List<CopilotInsight> Insights             { get; set; } = new();
    public bool                 HasActionableInsight => Insights != null && Insights.Count > 0;
    public DateTime             ProcessedAtUtc       { get; set; } = DateTime.UtcNow;
}
