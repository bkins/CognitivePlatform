using System;

namespace CognitivePlatform.Api.Domains.Agent;

public class AgentJob
{
    public string          Id             { get; set; } = Guid.NewGuid().ToString("N");
    public string          Prompt         { get; set; } = string.Empty;
    public AgentJobStatus  Status         { get; set; } = AgentJobStatus.Pending;
    public string?         Response       { get; set; }
    public string?         ConversationId { get; set; }
    public string?         Model          { get; set; }
    public DateTimeOffset  CreatedUtc     { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedUtc     { get; set; }
    public DateTimeOffset? CompletedUtc   { get; set; }
    public string?         Error          { get; set; }
    public bool            IsDeleted      { get; set; }
    public DateTimeOffset? DeletedUtc     { get; set; }
}
