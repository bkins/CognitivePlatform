namespace CognitivePlatform.Api.Automation;

public sealed class EngineeringPipelineSettings
{
    public string  RepositoryPath    { get; init; } = string.Empty;
    public string  WorkspaceRoot     { get; init; } = @"C:\CP\Data\Dev\EngineeringWorkspaces";
    public int     MaxElapsedMinutes { get; init; } = 30;
    public int     MaxModelRequests  { get; init; } = 20;
    public int     MaxRepairAttempts { get; init; } = 3;
    public decimal MaxSpendUsd       { get; init; }
}
