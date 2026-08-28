using System;
using System.Collections.Generic;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Conversations;

/// <summary>
/// A candidate memory extracted from conversation transcripts and structured analyses.
/// Represents provisional knowledge with strict provenance backlinks before user confirmation.
/// </summary>
public sealed class ConversationMemoryCandidate
{
    public Guid               Id                         { get; set; } = Guid.NewGuid();
    public Guid               ConversationId             { get; set; }
    public Guid?              AnalysisId                 { get; set; }
    public string             Category                   { get; set; } = "Fact";
    public string             Content                    { get; set; } = string.Empty;
    public string?            Speaker                    { get; set; }
    public List<Guid>         SourceTranscriptSegmentIds { get; set; } = new();
    public double             Confidence                 { get; set; } = 1.0;
    public DateTime           ExtractedAtUtc             { get; set; } = DateTime.UtcNow;
    public MemoryState        State                      { get; set; } = MemoryState.Provisional;
    public bool               IsDeleted                  { get; set; }
    public DateTime?          DeletedUtc                 { get; set; }
}
