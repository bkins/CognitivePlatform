namespace CognitivePlatform.Api.Conversation;

/// <summary>Session-local pre-action intent; selected-action parameter state remains PendingAction.</summary>
public sealed class PendingOrganizationIntent
{
    public string OriginalInput { get; init; } = string.Empty;
    public string[] OfferedAreas { get; init; } = [];
    public string? SelectedArea { get; set; }
    public int UnclearReplies { get; set; }
}
