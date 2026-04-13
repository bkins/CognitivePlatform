namespace CognitivePlatform.Api.Domains.DailyRecord;

public sealed class ParsedDailyCommand
{
    public DailyCommandType CommandType { get; init; } = DailyCommandType.Unknown;
    public string           BodyText    { get; init; } = string.Empty;
    public List<string>     Tasks       { get; init; } = new();
    public List<string>     Tags        { get; init; } = new();
    public string?          Mood        { get; init; }
    public int?             MoodScore   { get; init; }
}
