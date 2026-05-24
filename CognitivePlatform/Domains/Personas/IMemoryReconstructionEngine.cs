using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

/// <summary>
/// Extracts memory fragments from live conversation turns, assigns confidence
/// scores, detects contradictions against existing canonical memories, and
/// generates cognitive archaeology prompts that surface memory excavation
/// questions during persona conversations.
/// </summary>
public interface IMemoryReconstructionEngine
{
    /// <summary>
    /// Scans a single conversation turn and returns any memory fragments that
    /// can be inferred about the persona — factual, emotional, sensory, behavioral,
    /// or narrative details. Returns an empty list when nothing extractable is found.
    /// </summary>
    Task<IReadOnlyList<PersonaMemory>> ExtractMemoryFragmentsAsync( Guid              personaId
                                                                  , string            conversationTurn
                                                                  , CancellationToken cancellationToken = default );

    /// <summary>
    /// Scores the supplied memory's confidence based on specificity, emotional
    /// weight, and internal consistency. Returns the same memory instance with
    /// <see cref="PersonaMemory.Confidence"/> updated.
    /// </summary>
    Task<PersonaMemory> AssignConfidenceAsync( PersonaMemory     memory
                                             , CancellationToken cancellationToken = default );

    /// <summary>
    /// Compares the candidate memory against all canonical and reinforced memories
    /// for the persona and returns any contradictions whose
    /// <see cref="MemoryContradiction.ConflictScore"/> exceeds 0.5.
    /// Returns an empty list when no conflicts are detected.
    /// </summary>
    Task<IReadOnlyList<MemoryContradiction>> DetectContradictionsAsync( Guid              personaId
                                                                       , PersonaMemory     candidateMemory
                                                                       , CancellationToken cancellationToken = default );

    /// <summary>
    /// Generates a single cognitive archaeology question suited to the current
    /// conversation turn and the persona's memory gaps — e.g. probing for a detail
    /// that is sparse or absent. Returns <c>null</c> when no useful question can
    /// be generated.
    /// </summary>
    Task<string?> GenerateExcavationPromptAsync( CanonicalPersona  persona
                                               , string            conversationTurn
                                               , CancellationToken cancellationToken = default );
}
