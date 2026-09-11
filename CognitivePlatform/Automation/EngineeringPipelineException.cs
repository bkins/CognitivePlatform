namespace CognitivePlatform.Api.Automation;

public sealed class EngineeringPipelineException : InvalidOperationException
{
    public EngineeringPipelineException(string message)
        : base(message)
    {
    }
}
