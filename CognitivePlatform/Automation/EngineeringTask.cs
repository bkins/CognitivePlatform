namespace CognitivePlatform.Api.Automation;

public sealed record EngineeringTask(
    EngineeringTaskKind      Kind,
    IReadOnlyList<EngineeringTaskKind> DependsOn);
