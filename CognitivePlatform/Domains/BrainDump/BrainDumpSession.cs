namespace CognitivePlatform.Api.Domains.BrainDump;

public class BrainDumpSession
{
    public string          Id              { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset  CreatedAt       { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset  UpdatedAt       { get; set; } = DateTimeOffset.UtcNow;
    public bool            IsDeleted       { get; set; } = false;
    public DateTimeOffset? DeletedUtc      { get; set; }
    public bool            Processed       { get; set; } = false;
    public DateTimeOffset? ProcessedAt     { get; set; }

    // The 7 therapeutic categories — all nullable so the user may skip any section
    public string? Avoidance        { get; set; }
    public string? Fears            { get; set; }
    public string? Frustrations     { get; set; }
    public string? Discouragements  { get; set; }
    public string? GoalsAndBarriers { get; set; }
    public string? HurtAndSorrow    { get; set; }
    public string? SelfCriticism    { get; set; }

    // IDs of objects created during the extraction pass
    public List<string> ExtractedTaskIds    { get; set; } = new();
    public List<string> ExtractedInsightIds { get; set; } = new();

    // LLM-generated structured summary written after extraction
    public string? ExtractionSummary { get; set; }
}
