namespace CognitivePlatform.Api.Domains.DailyRecord;

public class DailyRecord
{
    /// <summary>ISO date string, e.g. "2026-03-14". Used as the store key for direct lookup.</summary>
    public string          Id                  { get; set; } = string.Empty;
    public DateOnly        Date                { get; set; }
    public DayPhase        Phase               { get; set; } = DayPhase.NotStarted;
    public bool            IsDeleted           { get; set; } = false;
    public DateTimeOffset? DeletedUtc          { get; set; }

    // Opening intent — set when the morning plan is submitted
    public string?         OpeningJournalId    { get; set; }
    public string?         OpeningText         { get; set; }
    public DateTimeOffset? OpenedAtUtc         { get; set; }

    // Intraday checkpoints
    public List<string>    CheckpointIds       { get; set; } = new();
    public List<string>    PlannedTaskIds      { get; set; } = new();
    public List<string>    ReactiveTaskIds     { get; set; } = new();

    // Closing reflection — set when the evening report is submitted
    public string?         ClosingJournalId    { get; set; }
    public string?         ClosingText         { get; set; }
    public DateTimeOffset? ClosedAtUtc         { get; set; }

    // Computed on close — stored for Insight Engine queries
    public int             PlannedTaskCount    { get; set; }
    public int             CompletedTaskCount  { get; set; }
    public int             ReactiveTaskCount   { get; set; }
    public double          CompletionRate      { get; set; }
    public string?         Mood                { get; set; }
    public int?            MoodScore           { get; set; }
}
