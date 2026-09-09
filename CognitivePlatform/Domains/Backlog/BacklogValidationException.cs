namespace CognitivePlatform.Api.Domains.Backlog;

public sealed class BacklogValidationException : Exception
{
    public BacklogValidationException(string message) : base(message)
    {
    }
}
