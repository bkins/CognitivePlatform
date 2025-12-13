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
}