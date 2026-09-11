namespace CognitivePlatform.Api.Automation;

public sealed record ProtectedActionClassification(
    ProtectedActionCategory Category,
    bool                    IsProtected,
    bool                    RequiresRollbackPlan);
