using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CognitivePlatform.Api.Domains.Conversations;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests.Conversations;

public class ConversationActionsTests
{
    private readonly Mock<IConversationService> _serviceMock;
    private readonly ConversationActions        _actions;

    public ConversationActionsTests()
    {
        _serviceMock = new Mock<IConversationService>();
        _actions     = new ConversationActions(_serviceMock.Object);
    }

    [Fact]
    public async Task QueryConversationMemory_ReturnsFormattedMemories_WhenMatchesExist()
    {
        var memories = new List<ConversationMemoryCandidate>
        {
            new()
            {
                Category = "Fact"
              , Content  = "Sarah started as Engineering Lead."
              , Speaker  = "Sarah"
            },
            new()
            {
                Category = "Commitment"
              , Content  = "Ben will send over documentation."
              , Speaker  = "Ben"
            }
        };

        _serviceMock.Setup(s => s.QueryMemoriesAsync("Sarah", default))
                    .ReturnsAsync(memories);

        var result = await _actions.QueryConversationMemory("Sarah");

        Assert.Contains("Found 2 memory reference(s)", result);
        Assert.Contains("Sarah started as Engineering Lead.", result);
        Assert.Contains("(from Sarah)", result);
    }

    [Fact]
    public async Task ListRecentConversations_ReturnsFormattedList()
    {
        var records = new List<ConversationRecord>
        {
            new() { Id = Guid.NewGuid(), Title = "Architecture Sync", RecordedAtUtc = DateTime.UtcNow }
          , new() { Id = Guid.NewGuid(), Title = "Sprint Retrospective", RecordedAtUtc = DateTime.UtcNow.AddDays(-1) }
        };

        _serviceMock.Setup(s => s.ListRecordingsAsync(default))
                    .ReturnsAsync(records);

        var result = await _actions.ListRecentConversations(5);

        Assert.Contains("Recent conversations (2 of 2):", result);
        Assert.Contains("Architecture Sync", result);
        Assert.Contains("Sprint Retrospective", result);
    }

    [Fact]
    public async Task GetConversationSummary_ReturnsFormattedSummaryAndDecisions()
    {
        var conversationId = Guid.NewGuid();
        var record         = new ConversationRecord { Id = conversationId, Title = "Q3 Roadmap", RecordedAtUtc = DateTime.UtcNow };
        var analysis       = new ConversationAnalysis
        {
            ConversationId = conversationId
          , Summary        = "Aligned on Q3 goals and architecture improvements."
          , Decisions      = new List<AnalysisDerivedItem>
            {
                new() { Content = "Target Phase 7 release for September." }
            }
          , ActionItems    = new List<AnalysisDerivedItem>
            {
                new() { Content = "Draft tech spec by Friday." }
            }
        };

        var details = new ConversationDetails
        {
            Record       = record
          , Analysis     = analysis
          , Participants = new List<ConversationParticipant> { new() { DisplayName = "Alice" } }
        };

        _serviceMock.Setup(s => s.GetRecordingAsync(conversationId, default))
                    .ReturnsAsync(record);
        _serviceMock.Setup(s => s.GetConversationDetailsAsync(conversationId, default))
                    .ReturnsAsync(details);

        var result = await _actions.GetConversationSummary(conversationId.ToString());

        Assert.Contains("# Q3 Roadmap", result);
        Assert.Contains("Aligned on Q3 goals", result);
        Assert.Contains("Target Phase 7 release for September.", result);
        Assert.Contains("Draft tech spec by Friday.", result);
    }
}
