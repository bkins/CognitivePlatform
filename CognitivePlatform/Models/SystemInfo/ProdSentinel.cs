namespace CognitivePlatform.Api.Models.SystemInfo;

public sealed class ProdSentinel
{
    public string   Environment  { get; init; } = "PROD";
    public DateTime CreatedAtUtc { get; init; }
    public string   CreatedBy    { get; init; } = "CognitivePlatform.Api";
}
