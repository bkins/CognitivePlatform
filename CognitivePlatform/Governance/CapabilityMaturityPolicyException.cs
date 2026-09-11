namespace CognitivePlatform.Api.Governance;

public sealed class CapabilityMaturityPolicyException : InvalidOperationException
{
    public CapabilityMaturityPolicyException(string message)
        : base(message)
    {
    }
}
