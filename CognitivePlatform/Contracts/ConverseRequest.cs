namespace CognitivePlatform.Api.Controllers;

public class ConverseRequest
{
    public string  SessionId { get; set; } = string.Empty;
    public string? Input      { get; set; }
}
