using System;
using System.Collections.Generic;

namespace CognitivePlatform.Api.Domains.Journal;

public sealed class JournalEntry
{
    public string         Id         { get; set; } = string.Empty;
    public string         Text       { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public List<string>?  Tags       { get; set; }
    public string?        Context    { get; set; }
    
    
    /// <summary>
    /// Free-form description of mood (e.g. "anxious but hopeful").
    /// </summary>
    public string? Mood { get; set; }

    /// <summary>
    /// Numeric mood score: 1 = very negative, 5 = very positive.
    /// </summary>
    public int? MoodScore { get; set; }

    /// <summary>
    /// Normalised mood level derived from MoodScore.
    /// </summary>
    public MoodLevel? MoodLevel { get; set; }

    /// <summary>
    /// Logical media paths related to this entry (e.g. "Media/{Id}/photo.jpg").
    /// </summary>
    public List<string> MediaPaths { get; set; } = new();
}

public enum MoodLevel
{
    VeryNegative = 1
  , Negative     = 2
  , Neutral      = 3
  , Positive     = 4
  , VeryPositive = 5
}