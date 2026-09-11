namespace CognitivePlatform.Api.Automation;

public interface IEngineeringWorkspaceProvisioner
{
    Task<EngineeringWorkspace> ProvisionAsync( string            engineeringRunId
                                              , string            baseRevision
                                              , CancellationToken cancellationToken = default );
}
