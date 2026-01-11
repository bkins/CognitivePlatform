namespace CognitivePlatform.Api.Contracts;

public class ConverseRequest
{
    public string  SessionId { get; set; } = string.Empty;
    public string? Input     { get; set; }
    public string? Model     { get; set; }
    public bool    FastPath  { get; set; }
    public bool    Streaming { get; set; }
}
