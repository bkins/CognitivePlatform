using System;
using System.Collections.Generic;

namespace CognitivePlatform.Api.Contracts;

public sealed class SyncResponseDto
{
    public string                          Workspace            { get; set; } = "Default";
    public DateTimeOffset                  SyncedAtUtc          { get; set; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<ConversationSyncDto> UpdatedConversations { get; set; } = Array.Empty<ConversationSyncDto>();
}

public sealed class ConversationSyncDto
{
    public string                                 Id           { get; set; } = string.Empty;
    public string                                 Title        { get; set; } = string.Empty;
    public DateTimeOffset                         LastUpdated  { get; set; }
    public int                                    MessageCount { get; set; }
    public IReadOnlyList<ConversationSyncMessageDto> Messages     { get; set; } = Array.Empty<ConversationSyncMessageDto>();
}

public sealed class ConversationSyncMessageDto
{
    public string         Sender           { get; set; } = string.Empty;
    public string         Content          { get; set; } = string.Empty;
    public string?        ActionName       { get; set; }
    public bool           WasFastPath      { get; set; }
    public string         ReasoningContent { get; set; } = string.Empty;
    public DateTimeOffset TimestampUtc     { get; set; }
}
