namespace CognitivePlatform.Api.Automation;

public sealed record ProtectedActionRequest(
    string                RunId,
    string                ActionName,
    EngineeringActionKind ActionKind);
