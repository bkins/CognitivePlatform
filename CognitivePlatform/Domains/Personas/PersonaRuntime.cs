using System.Text;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public class PersonaRuntime : IPersonaRuntime
{
    private readonly IPersonaService          _personaService;
    private readonly IPersonaStore            _personaStore;
    private readonly IPersonaBehaviorPolicy   _behaviorPolicy;
    private readonly IPersonaModelSelector?   _modelSelector;
    private readonly IModelCapabilityRegistry? _capabilityRegistry;

    public PersonaRuntime( IPersonaService           personaService
                         , IPersonaStore              personaStore
                         , IPersonaBehaviorPolicy     behaviorPolicy
                         , IPersonaModelSelector?     modelSelector      = null
                         , IModelCapabilityRegistry?  capabilityRegistry = null )
    {
        _personaService     = personaService     ?? throw new ArgumentNullException(nameof(personaService));
        _personaStore       = personaStore       ?? throw new ArgumentNullException(nameof(personaStore));
        _behaviorPolicy     = behaviorPolicy     ?? throw new ArgumentNullException(nameof(behaviorPolicy));
        _modelSelector      = modelSelector;
        _capabilityRegistry = capabilityRegistry;
    }

    public Task<string> BuildSystemPromptAsync(CanonicalPersona persona, CancellationToken cancellationToken = default)
    {
        var configuration = _behaviorPolicy.ApplyGuardrails(persona.Configuration);
        var prompt        = BuildPromptString(persona, configuration);
        return Task.FromResult(prompt);
    }

    public async Task<IReadOnlyList<PersonaMemory>> RetrieveRelevantMemoriesAsync(
        CanonicalPersona  persona
      , string            userMessage
      , int               maxMemories       = 10
      , CancellationToken cancellationToken = default )
    {
        var allMemories = await _personaStore.GetMemoriesAsync(persona.Id, cancellationToken);

        return allMemories
            .Where(memory => memory.State != MemoryState.Contradicted)
            .OrderByDescending(memory => (memory.Confidence * 0.6f) + (memory.EmotionalWeight * 0.4f))
            .Take(maxMemories)
            .ToList();
    }

    public async Task<PersonaConversationContext> BuildConversationContextAsync(
        Guid              personaId
      , string            userMessage
      , CancellationToken cancellationToken = default )
    {
        var persona = await _personaService.GetAsync(personaId, cancellationToken);

        if (persona is null)
            throw new InvalidOperationException($"Persona '{personaId}' not found.");

        var guardedConfiguration    = _behaviorPolicy.ApplyGuardrails(persona.Configuration);
        var isResurrectionViolation = _behaviorPolicy.IsResurrectionZone(persona.Configuration);
        var systemPrompt            = BuildPromptString(persona, guardedConfiguration);
        var relevantMemories        = await RetrieveRelevantMemoriesAsync(persona, userMessage, cancellationToken: cancellationToken);

        string? preferredModel = null;
        bool    promptAdapted  = false;

        if (_modelSelector is not null)
        {
            var defaultScore   = new PersonaStabilityScore { PersonaId = persona.Id, Score = 1.0f };
            preferredModel     = _modelSelector.SelectModel(persona, defaultScore);

            if (!string.IsNullOrWhiteSpace(preferredModel) && _capabilityRegistry is not null)
            {
                var profile = _capabilityRegistry.GetByModel(preferredModel);

                if (profile is not null)
                {
                    var adapted = _modelSelector.AdaptPromptForProvider(systemPrompt, profile);

                    if (!string.Equals(adapted, systemPrompt, StringComparison.Ordinal))
                    {
                        systemPrompt  = adapted;
                        promptAdapted = true;
                    }
                }
            }
        }

        return new PersonaConversationContext
               {
                       PersonaId                   = persona.Id
                     , PersonaName                 = persona.Name
                     , SystemPrompt                = systemPrompt
                     , RelevantMemories            = relevantMemories
                     , Configuration               = guardedConfiguration
                     , IsResurrectionZoneViolation = isResurrectionViolation
                     , PreferredModelName          = preferredModel
                     , PromptAdapted               = promptAdapted
               };
    }

    private static string BuildPromptString(CanonicalPersona persona, PersonaConfiguration configuration)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"You are embodying {persona.Name}.");

        if (!string.IsNullOrWhiteSpace(persona.ScenarioDescription))
            sb.AppendLine($"Scenario: {persona.ScenarioDescription}");

        if (!string.IsNullOrWhiteSpace(persona.Personality.Description))
            sb.AppendLine($"Personality: {persona.Personality.Description}");

        if (persona.Personality.Traits.Count > 0)
            sb.AppendLine($"Traits: {string.Join(", ", persona.Personality.Traits)}");

        if (!string.IsNullOrWhiteSpace(persona.RelationshipState.RelationshipType))
            sb.AppendLine($"Relationship: {persona.RelationshipState.RelationshipType}");

        if (!string.IsNullOrWhiteSpace(persona.EmotionalState.DominantEmotion))
            sb.AppendLine($"Emotional tone: {persona.EmotionalState.DominantEmotion}");

        sb.AppendLine();
        sb.AppendLine("Behavioral guidance:");
        sb.AppendLine($"- Immersion depth: {FormatLevel(configuration.ImmersionLevel)} — stay in character at this intensity.");
        sb.AppendLine($"- Emotional reciprocity: {FormatLevel(configuration.EmotionalReciprocity)} — mirror the emotional register of the conversation accordingly.");

        if (configuration.UncertaintyVisibility > 0.5f)
            sb.AppendLine("- You may acknowledge gaps or uncertainties in your memory when they arise.");
        else
            sb.AppendLine("- Speak with confidence; do not surface uncertainty unless directly pressed.");

        sb.AppendLine($"- Historical strictness: {FormatLevel(configuration.HistoricalStrictness)} — stay close to what is known about this person.");

        return sb.ToString().TrimEnd();
    }

    private static string FormatLevel(float value) =>
        value switch
        {
            >= 0.85f => "high"
          , >= 0.55f => "moderate"
          , _        => "low"
        };
}
