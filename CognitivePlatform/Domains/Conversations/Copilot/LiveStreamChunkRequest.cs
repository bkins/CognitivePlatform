using System;

namespace CognitivePlatform.Api.Domains.Conversations.Copilot;

public sealed class LiveStreamChunkRequest
{
    public int    ChunkIndex      { get; set; }
    public double OffsetSeconds   { get; set; }
    public double DurationSeconds { get; set; } = 2.5;
    public string PriorContext    { get; set; } = string.Empty;
}
