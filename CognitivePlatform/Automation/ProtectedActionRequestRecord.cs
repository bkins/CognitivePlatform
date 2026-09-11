namespace CognitivePlatform.Api.Automation;

public sealed record ProtectedActionRequestRecord
{
    public string                  Id                   { get; init; } = Guid.NewGuid().ToString("N");
    public string                  RunId                { get; init; } = string.Empty;
    public string                  ActionName           { get; init; } = string.Empty;
    public EngineeringActionKind   ActionKind           { get; init; }
    public ProtectedActionCategory Category             { get; init; }
    public bool                    IsProtected          { get; init; }
    public bool                    RequiresRollbackPlan { get; init; }
    public ProtectedActionStatus   Status               { get; init; }
    public string?                 ApprovedBy           { get; init; }
    public string?                 ApprovalReason       { get; init; }
    public string?                 RollbackPlan         { get; init; }
    public DateTimeOffset          CreatedUtc           { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset          UpdatedUtc           { get; init; } = DateTimeOffset.UtcNow;
}
