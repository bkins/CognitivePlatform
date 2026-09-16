namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed class TtrImportValidationException : Exception
{
    public TtrImportValidationException(string message) : base(message)
    {
    }
}
