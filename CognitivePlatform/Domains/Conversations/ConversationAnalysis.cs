namespace CognitivePlatform.Api.Domains.Conversations;

/// <summary>
/// Top-level analysis aggregate produced by <see cref="IConversationAnalyzer"/>.
/// One analysis per conversation — regeneration overwrites the previous result.
///
/// Invariant: The transcript is evidence; the analysis is interpretation.
/// Analysis items are always re-generatable and never mutate the raw transcript.
/// </summary>
public class ConversationAnalysis
{
    public Guid                     Id                  { get; set; } = Guid.NewGuid();
    public Guid                     ConversationId      { get; set; }
    public string                   Summary             { get; set; } = string.Empty;
    public List<AnalysisDerivedItem> Topics              { get; set; } = new();
    public List<AnalysisDerivedItem> Questions           { get; set; } = new();
    public List<AnalysisDerivedItem> Decisions           { get; set; } = new();
    public List<AnalysisDerivedItem> ActionItems         { get; set; } = new();
    public List<AnalysisDerivedItem> ImportantStatements { get; set; } = new();
    public AnalysisStatus           Status              { get; set; } = AnalysisStatus.NotAnalyzed;
    public DateTime                 CreatedAtUtc        { get; set; } = DateTime.UtcNow;
    public DateTime?                AnalyzedAtUtc       { get; set; }
    public string?                  ModelUsed           { get; set; }
    public string?                  ErrorMessage        { get; set; }
    public bool                     IsDeleted           { get; set; }
    public DateTime?                DeletedUtc          { get; set; }
}
