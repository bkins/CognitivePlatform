using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public class PersonaActions
{
    private static readonly AsyncLocal<string?> _sessionIdLocal = new();
    private static string? _sessionId
    {
        get => _sessionIdLocal.Value;
        set => _sessionIdLocal.Value = value;
    }

    private readonly IPersonaService        _personaService;
    private readonly IPersonaSessionManager _sessionManager;

    public PersonaActions( IPersonaService        personaService
                         , IPersonaSessionManager  sessionManager )
    {
        _personaService = personaService ?? throw new ArgumentNullException(nameof(personaService));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    public static void SetSessionId(string? sessionId) =>
        _sessionId = sessionId;

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
        var personas   = await _personaService.ListAsync();
        var searchTerm = personaNameOrId.Trim();

        var persona = personas.FirstOrDefault(candidate =>
            candidate.Name.Equals(searchTerm, StringComparison.OrdinalIgnoreCase)
         || candidate.Id.ToString().Equals(searchTerm, StringComparison.OrdinalIgnoreCase));

        if (persona is null)
            return $"No persona found matching '{personaNameOrId}'. Create one first with 'create persona {personaNameOrId}'.";

        var memory  = await _personaService.AddMemoryAsync(persona.Id, content, type, userAsserted);
        var preview = content.Length > 60 ? $"{content[..60]}..." : content;

        return $"Memory added to '{persona.Name}' [{memory.Type}]: {preview}";
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Begins a conversation session with a persona by name, entering persona mode for the current conversation."
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

        if (_sessionId is not null)
            _sessionManager.SetActivePersona(_sessionId, persona.Id);

        var sb = new StringBuilder();

        var scenarioContext = !string.IsNullOrWhiteSpace(persona.ScenarioDescription)
                                     ? $" {persona.ScenarioDescription}"
                                     : string.Empty;

        sb.AppendLine($"You're now connected with {persona.Name}.{scenarioContext}");
        sb.AppendLine();
        sb.Append("Say anything to begin.");

        return sb.ToString().TrimEnd();
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Ends the current persona conversation session, returning to normal conversation mode."
          , Examples         =
            [
                    "End conversation."
                  , "Stop talking to this persona."
                  , "Disconnect from persona."
                  , "Exit persona mode."
                  , "Stop speaking with Sarah."
            ]
          , Category         = "Personas")]
    public string EndPersonaConversation()
    {
        if (_sessionId is null)
            return "No active persona conversation to end.";

        if (!_sessionManager.IsPersonaConversation(_sessionId))
            return "No active persona conversation to end.";

        _sessionManager.ClearActivePersona(_sessionId);

        return "Persona conversation ended. You're back in normal mode.";
    }
}
