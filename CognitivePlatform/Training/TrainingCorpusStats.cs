namespace CognitivePlatform.Api.Training;

public class TrainingCorpusStats
{
    public int    TotalRecords   { get; set; }
    public int    SucceededCount { get; set; }
    public double SuccessRate    { get; set; }
    public double AvgLatencyMs   { get; set; }

    public IList<DailyRecordCount>  DailyCounts  { get; set; } = [];
    public IList<ActionRecordCount> ActionCounts { get; set; } = [];
}

public class DailyRecordCount
{
    public string Day         { get; set; } = string.Empty;
    public int    RecordCount { get; set; }
}

public class ActionRecordCount
{
    public string Action      { get; set; } = string.Empty;
    public int    RecordCount { get; set; }
}
