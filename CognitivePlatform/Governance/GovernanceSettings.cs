namespace CognitivePlatform.Api.Governance;

public sealed class GovernanceSettings
{
    public string NamedProductOwner { get; init; } = "Ben";

    public int Cml3MinimumVerifiedRunCount { get; init; } = 3;
}
