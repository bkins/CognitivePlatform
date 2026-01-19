using System;
using System.Collections.Generic;

namespace CognitivePlatform.Api.Domains.Journal;

public sealed class JournalEntry
{
    public string Id { get; init; } = default!;

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTime? DeletedUtc    { get; set; }
    public string?   DeletedReason { get; set; }
}


public enum MoodLevel
{
    VeryNegative = 1
  , Negative     = 2
  , Neutral      = 3
  , Positive     = 4
  , VeryPositive = 5
}