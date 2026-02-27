namespace CognitivePlatform.Api.Contracts;

public class ProcessedRequest
{
    public string Id { get; set; } = string.Empty; // = ClientRequestId (N format)

    public string ResponseJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }
}

