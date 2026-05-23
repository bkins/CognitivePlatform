using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public class PersonaActions
{
    private readonly IPersonaService _personaService;

    public PersonaActions(IPersonaService personaService)
    {
        _personaService = personaService ?? throw new ArgumentNullException(nameof(personaService));
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Creates a new synthetic persona for modeling another person, given a name and optional scenario."
          , Examples         =
            [
                    "Create a persona named Sarah."
                  , "Build a persona for my colleague Alex."
                  , "Add a person called Emma in a childhood memory scenario."
                  , "New persona: my old friend James."
                  , "Model a person named Taylor."
            ]
          , Category         = "Personas"
          , AllowsClarification = true
          , IsReplayable     = true)]
    public async Task<string> CreatePersona(
        [NaturalLanguageParam(Description = "The name of the persona to create."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string name
      , [NaturalLanguageParam(Description = "Optional description of the scenario or context for this persona."
                            , Optional    = true
                            , AllowEmpty  = true)]
        string? scenarioDescription = null )
    {
        var persona = await _personaService.CreateAsync(name, scenarioDescription);

        return $"Persona '{persona.Name}' created (id: {persona.Id}).";
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Adds a memory fragment to an existing persona — a fact, feeling, or event associated with that person."
          , Examples         =
            [
                    "Remember about Sarah that she loved hiking."
                  , "Add memory: Alex used to work in finance."
                  , "They used to meet every Friday for coffee."
                  , "Add a memory for James — he was afraid of thunderstorms."
                  , "Record that Emma's emotional tone was always warm."
            ]
          , Category         = "Personas"
          , AllowsClarification = true
          , IsReplayable     = true)]
    public async Task<string> AddPersonaMemory(
        [NaturalLanguageParam(Description = "The name or id of the persona this memory belongs to."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string personaNameOrId
      , [NaturalLanguageParam(Description = "The memory content to record."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string content
      , [NaturalLanguageParam(Description = "The type of memory: Historical, Emotional, Sensory, Behavioral, Narrative, Hypothetical, or Symbolic."
                            , Optional    = true)]
        MemoryType type = MemoryType.Narrative
      , [NaturalLanguageParam(Description = "Whether this memory was directly stated by the user (true) or should be treated as inferred (false)."
                            , Optional    = true
                            , DefaultValue = true)]
        bool userAsserted = true )
    {
        var personas = await _personaService.ListAsync();

        var persona = personas.FirstOrDefault(candidate =>
            candidate.Name.Equals(personaNameOrId.Trim(), StringComparison.OrdinalIgnoreCase)
         || candidate.Id.ToString().Equals(personaNameOrId.Trim(), StringComparison.OrdinalIgnoreCase));

        if (persona is null)
            return $"No persona found matching '{personaNameOrId}'. Create one first with 'create persona {personaNameOrId}'.";

        var memory  = await _personaService.AddMemoryAsync(persona.Id, content, type, userAsserted);
        var preview = content.Length > 60 ? $"{content[..60]}..." : content;

        return $"Memory added to '{persona.Name}' [{memory.Type}]: {preview}";
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Begins a conversation session with a persona by name, returning a summary of who they are."
          , Examples         =
            [
                    "Talk to Sarah."
                  , "Speak with Alex."
                  , "Connect with my persona James."
                  , "Start talking to Emma."
                  , "Begin a conversation with Taylor."
            ]
          , Category         = "Personas"
          , AllowsClarification = true)]
    public async Task<string> BeginPersonaConversation(
        [NaturalLanguageParam(Description = "The name of the persona to begin a conversation with."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string personaName )
    {
        var personas = await _personaService.ListAsync();

        var persona = personas.FirstOrDefault(candidate =>
            candidate.Name.Equals(personaName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (persona is null)
            return $"No persona found matching '{personaName}'. Create one first with 'create persona {personaName}'.";

        var sb = new StringBuilder();
        sb.AppendLine($"# Persona: {persona.Name}");

        if (!string.IsNullOrWhiteSpace(persona.ScenarioDescription))
            sb.AppendLine($"**Scenario:** {persona.ScenarioDescription}");

        if (!string.IsNullOrWhiteSpace(persona.RelationshipState.RelationshipType))
            sb.AppendLine($"**Relationship:** {persona.RelationshipState.RelationshipType}");

        if (!string.IsNullOrWhiteSpace(persona.EmotionalState.DominantEmotion))
            sb.AppendLine($"**Dominant emotion:** {persona.EmotionalState.DominantEmotion}");

        sb.AppendLine();
        sb.AppendLine($"_Persona loaded (id: {persona.Id}). Conversational runtime is Phase B — this is a Phase A context acknowledgement._");

        return sb.ToString().TrimEnd();
    }
}
