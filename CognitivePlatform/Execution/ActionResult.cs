namespace CognitivePlatform.Api.Execution;

public sealed class ActionResult
{
    public bool         Success { get; set; }
    public string       Message { get; set; } = string.Empty;
    public List<string> Logs    { get; set; } = new();
    public object?      Data    { get; set; }
}
