namespace CognitivePlatform.Api.Domains.DailyRecord;

public class DailyCheckpoint
{
    public string          Id               { get; set; } = Guid.NewGuid().ToString("N");
    public string          DailyRecordId    { get; set; } = string.Empty;
    public string?         JournalEntryId   { get; set; }
    public DateTimeOffset  CreatedAtUtc     { get; set; } = DateTimeOffset.UtcNow;
    public string          Text             { get; set; } = string.Empty;
    public List<string>    CompletedTaskIds { get; set; } = new();
    public List<string>    AddedTaskIds     { get; set; } = new();
}
