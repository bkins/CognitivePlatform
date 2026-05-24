using System.Text;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public class DreamModeAdapter : IDreamModeAdapter
{
    private const string DreamFramingInstruction =
        "This conversation takes place in a symbolic, non-literal space. "
      + "The person you are embodying may appear differently than remembered — "
      + "imagery and metaphor are welcome, and the usual rules of time and place need not apply. "
      + "Speak from a place of feeling rather than fact. "
      + "You are not required to be historically precise; you are required to be emotionally true.";

    public string BuildDreamPrompt( string                      userMessage
                                  , CanonicalPersona            persona
                                  , IEnumerable<PersonaMemory>  relevantMemories )
    {
        var memoriesArray = relevantMemories as PersonaMemory[] ?? relevantMemories.ToArray();

        var sb = new StringBuilder();

        sb.AppendLine($"[Dream Space — {persona.Name}]");
        sb.AppendLine(DreamFramingInstruction);
        sb.AppendLine();

        if (memoriesArray.Length > 0)
        {
            sb.AppendLine("Emotional echoes available in this space:");

            foreach (var memory in memoriesArray)
                sb.AppendLine($"  — {memory.Content}");

            sb.AppendLine();
        }

        sb.AppendLine("Message from the dreamer:");
        sb.Append(userMessage);

        return sb.ToString();
    }

    public bool IsDreamMode(PersonaConversationContext context) =>
        context.ActiveMode == ConversationMode.DreamMode;
}
