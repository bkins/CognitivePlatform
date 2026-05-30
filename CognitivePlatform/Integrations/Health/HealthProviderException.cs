using System.Net;

namespace CognitivePlatform.Api.Integrations.Health;

public sealed class HealthProviderException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public HealthProviderException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
