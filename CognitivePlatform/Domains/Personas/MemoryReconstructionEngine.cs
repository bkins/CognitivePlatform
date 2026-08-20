using System.Text.Json;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Personas.Models;
using CognitivePlatform.Api.Interpreter;

namespace CognitivePlatform.Api.Domains.Personas;

/// <summary>
/// LLM-backed implementation of the memory reconstruction pipeline.
/// All LLM calls are routed through <see cref="ILlmRouter"/> so the engine
/// never talks to a provider directly and benefits from the standard
/// capacity-routing and model-selection machinery.
/// </summary>
public class MemoryReconstructionEngine : IMemoryReconstructionEngine
{
    private readonly ILlmRouter   _llmRouter;
    private readonly IPersonaStore _personaStore;

    public MemoryReconstructionEngine( ILlmRouter    llmRouter
                                     , IPersonaStore  personaStore )
    {
        _llmRouter    = llmRouter    ?? throw new ArgumentNullException(nameof(llmRouter));
        _personaStore = personaStore ?? throw new ArgumentNullException(nameof(personaStore));
    }

    // ----------------------------------------------------------------
    // ExtractMemoryFragmentsAsync
    // ----------------------------------------------------------------

    public async Task<IReadOnlyList<PersonaMemory>> ExtractMemoryFragmentsAsync(
        Guid              personaId
      , string            conversationTurn
      , CancellationToken cancellationToken = default )
    {
        if (conversationTurn.HasNoValue())
            return [];

        var prompt = BuildExtractionPrompt(personaId, conversationTurn);
        var context = BuildInternalContext(personaId, "extract");

        try
        {
            var response = await _llmRouter.SendAsync(prompt, context, TaskComplexity.Standard, cancellationToken)
                                           .ConfigureAwait(false);

            return ParseExtractedFragments(personaId, response.Content);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }

    // ----------------------------------------------------------------
    // AssignConfidenceAsync
    // ----------------------------------------------------------------

    public async Task<PersonaMemory> AssignConfidenceAsync(
        PersonaMemory     memory
      , CancellationToken cancellationToken = default )
    {
        var prompt  = BuildConfidencePrompt(memory);
        var context = BuildInternalContext(memory.PersonaId, "confidence");

        try
        {
            var response  = await _llmRouter.SendAsync(prompt, context, TaskComplexity.Light, cancellationToken)
                                            .ConfigureAwait(false);

            memory.Confidence = ParseConfidenceScore(response.Content, memory.Confidence);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Preserve the existing confidence value on failure.
        }

        return memory;
    }

    // ----------------------------------------------------------------
    // DetectContradictionsAsync
    // ----------------------------------------------------------------

    public async Task<IReadOnlyList<MemoryContradiction>> DetectContradictionsAsync(
        Guid              personaId
      , PersonaMemory     candidateMemory
      , CancellationToken cancellationToken = default )
    {
        var allMemories = await _personaStore.GetMemoriesAsync(personaId, cancellationToken)
                                             .ConfigureAwait(false);

        var stableMemories = allMemories
            .Where(memory => memory.State == MemoryState.Canonical
                          || memory.State == MemoryState.Reinforced)
            .ToList();

        if (stableMemories.Count == 0)
            return [];

        var contradictions = new List<MemoryContradiction>();

        foreach (var existingMemory in stableMemories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var prompt  = BuildContradictionPrompt(existingMemory, candidateMemory);
            var context = BuildInternalContext(personaId, "contradict");

            try
            {
                var response = await _llmRouter.SendAsync(prompt, context, TaskComplexity.Light, cancellationToken)
                                               .ConfigureAwait(false);

                var contradiction = ParseContradiction(existingMemory.Id, candidateMemory.Id, response.Content);

                if (contradiction is not null && contradiction.ConflictScore > 0.5f)
                    contradictions.Add(contradiction);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Skip this pair on error — partial results are better than none.
            }
        }

        return contradictions;
    }

    // ----------------------------------------------------------------
    // GenerateExcavationPromptAsync
    // ----------------------------------------------------------------

    public async Task<string?> GenerateExcavationPromptAsync(
        CanonicalPersona  persona
      , string            conversationTurn
      , CancellationToken cancellationToken = default )
    {
        if (conversationTurn.HasNoValue())
            return null;

        var allMemories = await _personaStore.GetMemoriesAsync(persona.Id, cancellationToken)
                                             .ConfigureAwait(false);

        var prompt  = BuildExcavationPrompt(persona, conversationTurn, allMemories);
        var context = BuildInternalContext(persona.Id, "excavate");

        try
        {
            var response = await _llmRouter.SendAsync(prompt, context, TaskComplexity.Light, cancellationToken)
                                           .ConfigureAwait(false);

            var question = response.Content?.Trim();

            return question.HasNoValue() || IsNullSentinel(question)
                       ? null
                       : question;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    // ----------------------------------------------------------------
    // Prompt builders
    // ----------------------------------------------------------------

    private static string BuildExtractionPrompt(Guid personaId, string conversationTurn)
    {
        return $"""
                You are analyzing a conversation turn to extract memory fragments about a specific person (persona id: {personaId}).

                Conversation turn:
                "{conversationTurn}"

                Identify any factual, emotional, sensory, behavioral, or narrative details about this person that can be inferred from the message.
                For each fragment found, output a JSON array where each element has these exact keys:
                  "content"  : the memory fragment as a plain sentence
                  "type"     : one of Historical, Emotional, Sensory, Behavioral, Narrative, Hypothetical, Symbolic

                If nothing can be extracted, output an empty JSON array: []

                Respond with ONLY the JSON array — no explanation, no markdown fences.
                """;
    }

    private static string BuildConfidencePrompt(PersonaMemory memory)
    {
        return $"""
                Score the confidence of the following memory fragment on a scale from 0.0 to 1.0.

                Memory: "{memory.Content}"
                Memory type: {memory.Type}

                Scoring guide:
                  0.3–0.5 : vague, symbolic, or ambiguous — hard to verify
                  0.6–0.8 : specific or sensory — moderately reliable
                  0.8–0.95: highly specific with emotional anchoring — very reliable

                Respond with ONLY a decimal number between 0.0 and 1.0 — no explanation.
                """;
    }

    private static string BuildContradictionPrompt(PersonaMemory existingMemory, PersonaMemory candidateMemory)
    {
        return $"""
                Determine whether these two memory fragments about the same person contradict each other.

                Existing memory : "{existingMemory.Content}"
                Candidate memory: "{candidateMemory.Content}"

                Output a JSON object with exactly two keys:
                  "contradicts" : true or false
                  "score"       : a decimal from 0.0 (no conflict) to 1.0 (direct contradiction)
                  "reason"      : one sentence explaining the conflict, or "" if there is none

                Respond with ONLY the JSON object — no explanation, no markdown fences.
                """;
    }

    private static string BuildExcavationPrompt(
        CanonicalPersona              persona
      , string                        conversationTurn
      , IReadOnlyList<PersonaMemory>  allMemories )
    {
        var memorySummary = allMemories.Count > 0
            ? string.Join("\n", allMemories.Take(15).Select(memory => $"- [{memory.Type}] {memory.Content}"))
            : "(no memories recorded yet)";

        return $"""
                You are helping reconstruct a persona named "{persona.Name}".

                Current conversation turn:
                "{conversationTurn}"

                Known memories so far:
                {memorySummary}

                Based on what the user just said and the gaps in the current memory set,
                generate ONE cognitive archaeology question that would help uncover a missing
                or unclear detail — for example probing the senses, emotions, or specific events.

                If no useful question can be generated, output exactly: NULL

                Respond with ONLY the question (or NULL) — no explanation.
                """;
    }

    // ----------------------------------------------------------------
    // Response parsers
    // ----------------------------------------------------------------

    private static IReadOnlyList<PersonaMemory> ParseExtractedFragments(Guid personaId, string? raw)
    {
        if (raw.HasNoValue())
            return [];

        try
        {
            var trimmed  = raw.Trim();
            var elements = JsonSerializer.Deserialize<List<ExtractedFragment>>(trimmed
                                                                             , JsonOptions);

            if (elements is null || elements.Count == 0)
                return [];

            var now = DateTime.UtcNow;

            return elements
                .Where(fragment => fragment.Content.HasValue())
                .Select(fragment => new PersonaMemory
                {
                    Id              = Guid.NewGuid()
                  , PersonaId       = personaId
                  , Content         = fragment.Content!.Trim()
                  , Type            = ParseMemoryType(fragment.Type)
                  , State           = MemoryState.Provisional
                  , Source          = MemorySource.ConversationRecollection
                  , UserAsserted    = true
                  , AIInferred      = false
                  , Confidence      = 0.5f
                  , CreatedUtc      = now
                  , LastModifiedUtc = now
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static float ParseConfidenceScore(string? raw, float fallback)
    {
        if (raw.HasNoValue())
            return fallback;

        var trimmed = raw.Trim();

        if (float.TryParse(trimmed
                         , global::System.Globalization.NumberStyles.Float
                         , global::System.Globalization.CultureInfo.InvariantCulture
                         , out var parsed))
        {
            return Math.Clamp(parsed, 0.0f, 1.0f);
        }

        return fallback;
    }

    private static MemoryContradiction? ParseContradiction(Guid existingId, Guid candidateId, string? raw)
    {
        if (raw.HasNoValue())
            return null;

        try
        {
            var result = JsonSerializer.Deserialize<ContradictionResult>(raw.Trim(), JsonOptions);

            if (result is null || !result.Contradicts)
                return null;

            return new MemoryContradiction
                   {
                           ExistingMemoryId  = existingId
                         , CandidateMemoryId = candidateId
                         , ConflictScore     = Math.Clamp(result.Score, 0.0f, 1.0f)
                         , Reason            = result.Reason ?? string.Empty
                   };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsNullSentinel(string value) =>
        value.EqualsIgnoreCase("NULL");

    private static MemoryType ParseMemoryType(string? raw) =>
        Enum.TryParse<MemoryType>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : MemoryType.Narrative;

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static ConversationContext BuildInternalContext(Guid personaId, string operationType) =>
        new($"persona-reconstruction-{personaId}-{operationType}");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // ----------------------------------------------------------------
    // Private DTO types for JSON deserialization
    // ----------------------------------------------------------------

    private sealed class ExtractedFragment
    {
        public string? Content { get; set; }
        public string? Type    { get; set; }
    }

    private sealed class ContradictionResult
    {
        public bool    Contradicts { get; set; }
        public float   Score       { get; set; }
        public string? Reason      { get; set; }
    }
}
