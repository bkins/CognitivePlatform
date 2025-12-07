namespace CognitivePlatform.Api.Models;

public class ParsedModelResponse
{
    public string?                    ActionName        { get; init; }
    public Dictionary<string, string> Parameters        { get; init; } = new();
    public string                     DebugInfo         { get; init; } = "";
    public string?                    Reason            { get; init; }
    public InterpreterFailureType     FailureType       { get; init; } = InterpreterFailureType.None;
    public IReadOnlyList<string>?     CandidateActions  { get; init; }
    public IReadOnlyList<string>?     MissingParameters { get; init; }
}