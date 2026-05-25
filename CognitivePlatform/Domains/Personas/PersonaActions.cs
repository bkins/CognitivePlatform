using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Domains.Personas.Models;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Domains.Personas;

[Domain(typeof(PersonasDomain))]
public class PersonaActions
{
    private static readonly AsyncLocal<string?> _sessionIdLocal = new();
    private static string? _sessionId
    {
        get => _sessionIdLocal.Value;
        set => _sessionIdLocal.Value = value;
    }

    private readonly IPersonaService            _personaService;
    private readonly IPersonaSessionManager    _sessionManager;
    private readonly IPersonaStore             _personaStore;
    private readonly IMemoryConfirmationQueue  _confirmationQueue;
    private readonly IAlternateTimelineService? _timelineService;
    private readonly IMemorySnapshotService?   _snapshotService;

    public PersonaActions( IPersonaService           personaService
                         , IPersonaSessionManager     sessionManager
                         , IPersonaStore              personaStore
                         , IMemoryConfirmationQueue   confirmationQueue
                         , IAlternateTimelineService? timelineService = null
                         , IMemorySnapshotService?    snapshotService = null )
    {
        _personaService    = personaService    ?? throw new ArgumentNullException(nameof(personaService));
        _sessionManager    = sessionManager    ?? throw new ArgumentNullException(nameof(sessionManager));
        _personaStore      = personaStore      ?? throw new ArgumentNullException(nameof(personaStore));
        _confirmationQueue = confirmationQueue ?? throw new ArgumentNullException(nameof(confirmationQueue));
        _timelineService   = timelineService;
        _snapshotService   = snapshotService;
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

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Confirms the next pending memory fragment for the active persona conversation, advancing it toward canonical status."
          , Examples         =
            [
                    "Yes that's right."
                  , "That's correct."
                  , "Confirm that memory."
                  , "Yes I remember."
                  , "That's accurate."
            ]
          , Category         = "Personas")]
    public async Task<string> ConfirmMemory()
    {
        if (_sessionId is null)
            return "No active persona conversation.";

        if (!_confirmationQueue.HasPending(_sessionId))
            return "No pending memories to confirm.";

        var memory = _confirmationQueue.Dequeue(_sessionId);

        if (memory is null)
            return "No pending memories to confirm.";

        await _personaStore.ConfirmMemoryAsync(memory.Id, memory.PersonaId);

        var preview = memory.Content.Length > 60
                          ? $"{memory.Content[..60]}..."
                          : memory.Content;

        return $"Memory confirmed [{memory.Type}]: {preview}";
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Lists the next few memory fragments awaiting confirmation for the active persona conversation."
          , Examples         =
            [
                    "What memories are pending?"
                  , "Show unconfirmed memories."
                  , "What do you remember so far?"
                  , "What memories are waiting?"
                  , "List pending memory fragments."
            ]
          , Category         = "Personas")]
    public string ListPendingMemories()
    {
        if (_sessionId is null)
            return "No active persona conversation.";

        var pending = _confirmationQueue.Peek(_sessionId, count: 3);

        if (pending.Count == 0)
            return "No memories are currently pending confirmation.";

        var sb = new StringBuilder();
        sb.AppendLine($"Pending memories ({pending.Count} shown):");

        foreach (var memory in pending)
        {
            var preview = memory.Content.Length > 80
                              ? $"{memory.Content[..80]}..."
                              : memory.Content;

            sb.AppendLine($"  [{memory.Type}] {preview} (confidence: {memory.Confidence:P0})");
        }

        return sb.ToString().TrimEnd();
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Sets the conversation mode for a persona conversation — controls how the exchange is framed (e.g. DreamMode, Reflection, FreeConversation)."
          , Examples         =
            [
                    "Switch to dream mode."
                  , "Enter reflection mode."
                  , "Go back to free conversation."
                  , "Set conversation mode to alternate timeline."
                  , "Activate therapeutic distance mode."
            ]
          , Category         = "Personas"
          , IsReplayable     = true)]
    public string SetConversationMode(
        [NaturalLanguageParam(Description = "The conversation ID to change mode for."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string conversationId
      , [NaturalLanguageParam(Description = "The conversation mode to activate: FreeConversation, Reconstruction, Reflection, AlternateTimeline, HistoricalReplay, DreamMode, or TherapeuticDistance."
                            , Optional    = false)]
        ConversationMode mode )
    {
        _sessionManager.SetConversationMode(conversationId, mode);

        return $"Conversation mode set to '{mode}'.";
    }

    [NaturalLanguageAction(
            Description      = "Creates an alternate timeline for a persona — a named 'what if' branch from the main narrative."
          , Examples         =
            [
                    "Create an alternate timeline for Sarah called 'stayed in town' with the premise she never left."
                  , "What if Emma had taken the job offer? Create a timeline called 'different path'."
                  , "Add an alternate timeline for James — 'the road not taken'."
            ]
          , Category         = "Personas"
          , AllowsClarification = true
          , IsReplayable     = true)]
    public async Task<string> CreateAlternateTimeline(
        [NaturalLanguageParam(Description = "The id of the persona this timeline belongs to."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string personaId
      , [NaturalLanguageParam(Description = "A short name for this alternate timeline."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string name
      , [NaturalLanguageParam(Description = "The premise or 'what if' premise of this timeline."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string premise )
    {
        if (_timelineService is null)
            return "Alternate timeline service is not available.";

        if (!Guid.TryParse(personaId, out var personaGuid))
            return $"'{personaId}' is not a valid persona id.";

        var timeline = await _timelineService.CreateTimelineAsync(personaGuid, name, premise, DateTime.UtcNow);

        return $"Alternate timeline '{timeline.Name}' created (id: {timeline.Id}). Premise: {premise}";
    }

    [NaturalLanguageAction(
            Description      = "Creates a memory snapshot for a persona — captures the current state of memories and configuration as a named, loadable branch reality."
          , Examples         =
            [
                    "Create a snapshot for Sarah called 'romanticized version'."
                  , "Save a snapshot of James's current memories as 'grounded realism'."
                  , "Snapshot Emma — name it 'dreamlike reconstruction'."
            ]
          , Category         = "Personas"
          , AllowsClarification = true
          , IsReplayable     = true)]
    public async Task<string> CreatePersonaSnapshot(
        [NaturalLanguageParam(Description = "The id of the persona to snapshot."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string personaId
      , [NaturalLanguageParam(Description = "A name for this snapshot."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string name
      , [NaturalLanguageParam(Description = "Optional notes about what this snapshot represents."
                            , Optional    = true
                            , AllowEmpty  = true)]
        string notes = "" )
    {
        if (_snapshotService is null)
            return "Snapshot service is not available.";

        if (!Guid.TryParse(personaId, out var personaGuid))
            return $"'{personaId}' is not a valid persona id.";

        var snapshot = await _snapshotService.CreateSnapshotAsync(personaGuid, name, notes);

        return $"Snapshot '{snapshot.Name}' created (id: {snapshot.Id}) with {snapshot.IncludedMemories.Count} memories captured.";
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Loads a memory snapshot into a conversation, causing subsequent persona turns to use the snapshot's memories and configuration instead of live state."
          , Examples         =
            [
                    "Load snapshot into this conversation."
                  , "Activate the romanticized snapshot."
                  , "Use snapshot version for this session."
            ]
          , Category         = "Personas"
          , IsReplayable     = true)]
    public async Task<string> LoadSnapshot(
        [NaturalLanguageParam(Description = "The id of the snapshot to load."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string snapshotId
      , [NaturalLanguageParam(Description = "The conversation id to load the snapshot into."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string conversationId )
    {
        if (_snapshotService is null)
            return "Snapshot service is not available.";

        if (!Guid.TryParse(snapshotId, out var snapshotGuid))
            return $"'{snapshotId}' is not a valid snapshot id.";

        await _snapshotService.LoadSnapshotAsync(snapshotGuid, conversationId);

        return $"Snapshot '{snapshotId}' loaded into conversation '{conversationId}'. Subsequent turns will use snapshot state.";
    }
}
