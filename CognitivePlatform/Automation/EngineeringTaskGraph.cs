namespace CognitivePlatform.Api.Automation;

public sealed record EngineeringTaskGraph(IReadOnlyList<EngineeringTask> Tasks)
{
    public static EngineeringTaskGraph CreateDefault()
    {
        return new EngineeringTaskGraph(
        [
            new(EngineeringTaskKind.Requirements, []),
            new(EngineeringTaskKind.Planning, [EngineeringTaskKind.Requirements]),
            new(EngineeringTaskKind.WorkspaceProvisioning, [EngineeringTaskKind.Planning]),
            new(EngineeringTaskKind.CodeGeneration, [EngineeringTaskKind.WorkspaceProvisioning]),
            new(EngineeringTaskKind.StaticAnalysis, [EngineeringTaskKind.CodeGeneration]),
            new(EngineeringTaskKind.Compilation, [EngineeringTaskKind.StaticAnalysis]),
            new(EngineeringTaskKind.UnitTests, [EngineeringTaskKind.Compilation]),
            new(EngineeringTaskKind.IntegrationTests, [EngineeringTaskKind.UnitTests]),
            new(EngineeringTaskKind.UiAutomation, [EngineeringTaskKind.IntegrationTests]),
            new(EngineeringTaskKind.RegressionTests, [EngineeringTaskKind.UiAutomation]),
            new(EngineeringTaskKind.AcceptanceTests, [EngineeringTaskKind.RegressionTests]),
            new(EngineeringTaskKind.PromotionReview, [EngineeringTaskKind.AcceptanceTests])
        ]);
    }
}
