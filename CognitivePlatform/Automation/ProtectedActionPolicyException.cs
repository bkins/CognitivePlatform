namespace CognitivePlatform.Api.Automation;

public sealed class ProtectedActionPolicyException : InvalidOperationException
{
    public ProtectedActionPolicyException(string message)
        : base(message)
    {
    }
}
