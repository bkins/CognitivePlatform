namespace CognitivePlatform.Api.Training;

public class InterpreterTrainingRecord
{
    public Guid   Id                    { get; set; } = Guid.NewGuid();
    public string UserInput             { get; set; } = "";
    public string NormalizedInput       { get; set; } = "";
    public string ModelOutput           { get; set; } = "";
    public string FinalResolvedAction   { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } = [];
    public bool   RequiredClarification { get; set; }
    public int    ClarificationCount    { get; set; }
    public bool   ExecutionSucceeded    { get; set; }
    public string FailureType           { get; set; } = "";
    public string ModelVersion          { get; set; } = "";
    public string PromptVersion         { get; set; } = "";
    public double LatencyMs             { get; set; }
    public DateTime TimestampUtc        { get; set; } = DateTime.UtcNow;
}
