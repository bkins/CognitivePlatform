using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Conversation;

public class ConversationContext
{
    public string?                    LastUserMessage       { get; set; }
    public string?                    LastActionName        { get; set; }
    public Dictionary<string, string> LastParameters        { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Metadata              { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string?                    LastInterpreterReason { get; set; }
    public string?                    LastInterpreterDebug  { get; set; }

    public InterpreterFailureType LastFailureType       { get; set; } = InterpreterFailureType.None;
    public List<string>           LastCandidateActions  { get; }      = new();
    public List<string>           LastMissingParameters { get; }      = new();

    public void Reset()
    {
        LastUserMessage       = null;
        LastActionName        = null;
        LastInterpreterReason = null;
        LastInterpreterDebug  = null;
        LastFailureType       = InterpreterFailureType.None;

        LastParameters.Clear();
        Metadata.Clear();
        LastCandidateActions.Clear();
        LastMissingParameters.Clear();
    }
}