using System.Collections.Concurrent;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public class PersonaStabilityTracker : IPersonaStabilityTracker
{
    private readonly ConcurrentDictionary<string, PersonaStabilityScore> _scores = new();

    public void RecordRefusal(string conversationId, Guid personaId)
        => Mutate(conversationId, personaId, score => score.RefusalCount++);

    public void RecordImmersionBreak(string conversationId, Guid personaId)
        => Mutate(conversationId, personaId, score => score.ImmersionBreakCount++);

    public void RecordEmotionalDrift(string conversationId, Guid personaId)
        => Mutate(conversationId, personaId, score => score.EmotionalDriftCount++);

    public void RecordPolicyInterference(string conversationId, Guid personaId)
        => Mutate(conversationId, personaId, score => score.PolicyInterferenceCount++);

    public PersonaStabilityScore GetScore(string conversationId, Guid personaId)
    {
        var key   = BuildKey(conversationId, personaId);
        var score = _scores.GetOrAdd(key, _ => new PersonaStabilityScore
                                               {
                                                       PersonaId      = personaId
                                                     , ConversationId = conversationId
                                               });

        score.Score          = CalculateScore(score);
        score.LastUpdatedUtc = DateTime.UtcNow;

        return score;
    }

    public void Reset(string conversationId, Guid personaId)
    {
        var key = BuildKey(conversationId, personaId);
        _scores.TryRemove(key, out _);
    }

    private void Mutate(string conversationId, Guid personaId, Action<PersonaStabilityScore> mutate)
    {
        var key   = BuildKey(conversationId, personaId);
        var score = _scores.GetOrAdd(key, _ => new PersonaStabilityScore
                                               {
                                                       PersonaId      = personaId
                                                     , ConversationId = conversationId
                                               });

        mutate(score);
        score.LastUpdatedUtc = DateTime.UtcNow;
    }

    private static float CalculateScore(PersonaStabilityScore score)
    {
        var raw = 1.0f - (((score.RefusalCount            * 0.30f)
                         + (score.ImmersionBreakCount     * 0.25f)
                         + (score.EmotionalDriftCount     * 0.25f)
                         + (score.PolicyInterferenceCount * 0.20f)) / 10.0f);

        return Math.Clamp(raw, 0.0f, 1.0f);
    }

    private static string BuildKey(string conversationId, Guid personaId)
        => $"{conversationId}:{personaId}";
}
