using System.Net;

namespace CognitivePlatform.Api.Integrations.FileSync;

public sealed class FileSyncProviderException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public FileSyncProviderException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
