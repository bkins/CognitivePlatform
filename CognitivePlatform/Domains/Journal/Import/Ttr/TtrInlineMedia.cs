namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed class TtrInlineMedia
{
    public string ContentType { get; init; } = string.Empty;
    public byte[] Bytes        { get; init; } = [];
}
