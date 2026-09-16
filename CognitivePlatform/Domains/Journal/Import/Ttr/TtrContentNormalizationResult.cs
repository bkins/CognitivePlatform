namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed class TtrContentNormalizationResult
{
    public string Markdown                    { get; init; } = string.Empty;
    public IReadOnlyList<TtrInlineMedia> InlineMedia { get; init; } = [];
    public IReadOnlyList<string> Warnings           { get; init; } = [];
}
