namespace CognitivePlatform.Api.Automation;

public sealed class ProtectedActionClassifier
{
    public ProtectedActionClassification Classify(EngineeringActionKind actionKind)
    {
        return actionKind switch
               {
                   EngineeringActionKind.SandboxWorkspaceProvisioning => new ProtectedActionClassification(ProtectedActionCategory.SandboxOperation, false, false)
                 , EngineeringActionKind.SandboxValidation            => new ProtectedActionClassification(ProtectedActionCategory.SandboxOperation, false, false)
                 , EngineeringActionKind.CapabilityMaturityPromotion  => new ProtectedActionClassification(ProtectedActionCategory.CapabilityMaturity, true, true)
                 , EngineeringActionKind.Merge                         => new ProtectedActionClassification(ProtectedActionCategory.DeploymentOrRelease, true, true)
                 , EngineeringActionKind.ReleasePromotion              => new ProtectedActionClassification(ProtectedActionCategory.DeploymentOrRelease, true, true)
                 , EngineeringActionKind.Deployment                    => new ProtectedActionClassification(ProtectedActionCategory.DeploymentOrRelease, true, true)
                 , EngineeringActionKind.ProductionAccess              => new ProtectedActionClassification(ProtectedActionCategory.ProductionAccess, true, true)
                 , EngineeringActionKind.SchemaMigration               => new ProtectedActionClassification(ProtectedActionCategory.SharedDataOrSchema, true, true)
                 , EngineeringActionKind.SharedDataMutation            => new ProtectedActionClassification(ProtectedActionCategory.SharedDataOrSchema, true, true)
                 , EngineeringActionKind.SecretOrConfigurationChange   => new ProtectedActionClassification(ProtectedActionCategory.SecretsOrConfiguration, true, true)
                 , EngineeringActionKind.IdentityOrPermissionChange    => new ProtectedActionClassification(ProtectedActionCategory.IdentityOrPermissions, true, true)
                 , EngineeringActionKind.ApprovalPolicyChange          => new ProtectedActionClassification(ProtectedActionCategory.IdentityOrPermissions, true, true)
                 , EngineeringActionKind.ExternalIntegrationActivation => new ProtectedActionClassification(ProtectedActionCategory.ExternalIntegrationOrOutboundTransfer, true, true)
                 , EngineeringActionKind.OutboundDataTransfer          => new ProtectedActionClassification(ProtectedActionCategory.ExternalIntegrationOrOutboundTransfer, true, true)
                 , EngineeringActionKind.SharedComputeAllocation       => new ProtectedActionClassification(ProtectedActionCategory.SpendOrSharedCompute, true, false)
                 , _                                                   => new ProtectedActionClassification(ProtectedActionCategory.Unclassified, true, false)
               };
    }
}
