namespace CognitivePlatform.Api.Domains.Backlog;

public sealed class BacklogConflictException : Exception
{
    public BacklogConflictException(string message) : base(message)
    {
    }
}
