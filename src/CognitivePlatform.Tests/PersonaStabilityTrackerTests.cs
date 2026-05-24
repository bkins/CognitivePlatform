using CognitivePlatform.Api.Domains.Personas;

namespace CognitivePlatform.Tests;

public class PersonaStabilityTrackerTests
{
    private readonly PersonaStabilityTracker _tracker = new();

    [Fact]
    public void GetScore_ReturnsFullScore_WhenNoEventsRecorded()
    {
        var conversationId = "conv-1";
        var personaId      = Guid.NewGuid();

        var score = _tracker.GetScore(conversationId, personaId);

        Assert.Equal(1.0f, score.Score);
        Assert.Equal(0,    score.RefusalCount);
        Assert.Equal(0,    score.ImmersionBreakCount);
        Assert.Equal(0,    score.EmotionalDriftCount);
        Assert.Equal(0,    score.PolicyInterferenceCount);
    }

    [Fact]
    public void RecordRefusal_ReducesScore()
    {
        var conversationId = "conv-refusal";
        var personaId      = Guid.NewGuid();

        _tracker.RecordRefusal(conversationId, personaId);
        var score = _tracker.GetScore(conversationId, personaId);

        Assert.Equal(1, score.RefusalCount);
        Assert.True(score.Score < 1.0f);
    }

    [Fact]
    public void RecordImmersionBreak_ReducesScore()
    {
        var conversationId = "conv-immersion";
        var personaId      = Guid.NewGuid();

        _tracker.RecordImmersionBreak(conversationId, personaId);
        var score = _tracker.GetScore(conversationId, personaId);

        Assert.Equal(1, score.ImmersionBreakCount);
        Assert.True(score.Score < 1.0f);
    }

    [Fact]
    public void RecordPolicyInterference_ReducesScore()
    {
        var conversationId = "conv-policy";
        var personaId      = Guid.NewGuid();

        _tracker.RecordPolicyInterference(conversationId, personaId);
        var score = _tracker.GetScore(conversationId, personaId);

        Assert.Equal(1, score.PolicyInterferenceCount);
        Assert.True(score.Score < 1.0f);
    }

    [Fact]
    public void Score_ClampedToZero_WhenManyEvents()
    {
        var conversationId = "conv-clamped";
        var personaId      = Guid.NewGuid();

        for (var iteration = 0; iteration < 50; iteration++)
        {
            _tracker.RecordRefusal(conversationId, personaId);
            _tracker.RecordImmersionBreak(conversationId, personaId);
            _tracker.RecordPolicyInterference(conversationId, personaId);
        }

        var score = _tracker.GetScore(conversationId, personaId);

        Assert.Equal(0.0f, score.Score);
    }

    [Fact]
    public void Reset_ClearsStateToDefault()
    {
        var conversationId = "conv-reset";
        var personaId      = Guid.NewGuid();

        _tracker.RecordRefusal(conversationId, personaId);
        _tracker.RecordImmersionBreak(conversationId, personaId);

        _tracker.Reset(conversationId, personaId);

        var score = _tracker.GetScore(conversationId, personaId);

        Assert.Equal(1.0f, score.Score);
        Assert.Equal(0,    score.RefusalCount);
        Assert.Equal(0,    score.ImmersionBreakCount);
    }
}
