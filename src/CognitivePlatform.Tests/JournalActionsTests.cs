using Moq;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.SystemPromptLogging;

namespace CognitivePlatform.Tests;

public class JournalActionsTests
{
    private readonly Mock<IJournalService>       _journalMock   = new();
    private readonly Mock<IJournalCommandParser> _parserMock    = new();
    private readonly Mock<ILlmClient>            _llmMock       = new();
    private readonly Mock<IPromptLogger>         _promptLogMock = new();
    private readonly JournalActions              _actions;

    public JournalActionsTests()
    {
        _actions = new JournalActions(_journalMock.Object
                                    , _parserMock.Object
                                    , _llmMock.Object
                                    , _promptLogMock.Object);
    }

    // ================================================================
    // SEARCH JOURNAL ENTRIES
    // ================================================================

    [Fact]
    public void SearchJournalEntries_ReturnsFormattedResult_WhenMatchesFound()
    {
        var entry = MakeEntryWithRevision("I spoke with Jake today.");
        _journalMock.Setup(svc => svc.SearchEntries("Jake"
                                                   , It.IsAny<DateTimeOffset?>()
                                                   , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision> { entry });

        var result = _actions.SearchJournalEntries("Jake");

        Assert.Contains("Jake",  result);
        Assert.Contains("Found 1 journal entry containing 'Jake':", result);
        Assert.Contains("I spoke with Jake today.", result);
    }

    [Fact]
    public void SearchJournalEntries_UsesPlural_WhenMultipleMatchesFound()
    {
        var entryWithRevision        = MakeEntryWithRevision("Talked to Jake.");
        var anotherEntryWithRevision = MakeEntryWithRevision("Jake called again.");
        
        _journalMock.Setup(svc => svc.SearchEntries("Jake"
                                                   , It.IsAny<DateTimeOffset?>()
                                                   , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision> { entryWithRevision, anotherEntryWithRevision });

        var result = _actions.SearchJournalEntries("Jake");

        Assert.Contains("Found 2 journal entries containing 'Jake':", result);
    }

    [Fact]
    public void SearchJournalEntries_ReturnsNoMatchMessage_WhenEmpty()
    {
        _journalMock.Setup(service => service.SearchEntries("Jake"
                                                          , It.IsAny<DateTimeOffset?>()
                                                          , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        var result = _actions.SearchJournalEntries("Jake");

        Assert.Equal("No journal entries found containing 'Jake'.", result);
    }

    [Fact]
    public void SearchJournalEntries_PassesDateRange_ToService()
    {
        _journalMock.Setup(service => service.SearchEntries(It.IsAny<string>()
                                                          , It.IsAny<DateTimeOffset?>()
                                                          , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        _actions.SearchJournalEntries("Jake", fromDate: "2026-01-01", toDate: "2026-01-31");

        _journalMock.Verify(service => service.SearchEntries("Jake"
                                                           , It.Is<DateTimeOffset?>(offset => offset != null)
                                                           , It.Is<DateTimeOffset?>(offset => offset != null))
                          , Times.Once);
    }

    // ================================================================
    // ANALYZE JOURNAL
    // ================================================================

    [Fact]
    public async Task AnalyzeJournal_ReturnsLlmResponse_WhenEntriesExist()
    {
        var entry = MakeEntryWithRevision("Felt frustrated with the project today.");
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>()
                                                 , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision> { entry });

        _llmMock.Setup(llm => llm.SendAsync(It.IsAny<string>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<CancellationToken>()))
                .ReturnsAsync("You seemed frustrated about the project.");

        var result = await _actions.AnalyzeJournal("What was I frustrated about?");

        Assert.Equal("You seemed frustrated about the project.", result);
    }

    [Fact]
    public async Task AnalyzeJournal_ReturnsNoEntriesMessage_WhenListIsEmpty()
    {
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>()
                                                 , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        var result = await _actions.AnalyzeJournal("What was I frustrated about?");

        Assert.Equal("No journal entries found for the specified date range.", result);
        _llmMock.Verify(llm => llm.SendAsync(It.IsAny<string>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<CancellationToken>())
                      , Times.Never);
    }

    [Fact]
    public async Task AnalyzeJournal_PromptContainsEntryText_AndQuestion()
    {
        var entry = MakeEntryWithRevision("Felt frustrated with the project today.");
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>()
                                                , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision> { entry });

        string? capturedPrompt = null;
        _llmMock.Setup(llm => llm.SendAsync(It.IsAny<string>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<CancellationToken>()))
                .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
                .ReturnsAsync("Some response.");

        await _actions.AnalyzeJournal("What was I frustrated about?");

        Assert.NotNull(capturedPrompt);
        Assert.Contains("Felt frustrated with the project today.", capturedPrompt);
        Assert.Contains("What was I frustrated about?",            capturedPrompt);
    }

    [Fact]
    public async Task AnalyzeJournal_PromptIncludesMoodAndTags_WhenPresent()
    {
        var entry = MakeEntryWithRevision("Ordinary day."
                                        , mood:      "anxious"
                                        , moodScore: 2
                                        , tags:      new[] { "work" });
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>()
                                                 , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision> { entry });

        string? capturedPrompt = null;
        _llmMock.Setup(llm => llm.SendAsync(It.IsAny<string>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<CancellationToken>()))
                .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
                .ReturnsAsync("Some response.");

        await _actions.AnalyzeJournal("How was I feeling?");

        Assert.NotNull(capturedPrompt);
        Assert.Contains("anxious", capturedPrompt);
        Assert.Contains("2",       capturedPrompt);
        Assert.Contains("work",    capturedPrompt);
    }

    // ================================================================
    // HELPERS
    // ================================================================

    private static JournalEntryWithRevision MakeEntryWithRevision( string    text
                                                                  , string?   mood      = null
                                                                  , int?      moodScore = null
                                                                  , string[]? tags      = null)
    {
        var entryId = Guid.NewGuid().ToString("N");
        var entry   = new JournalEntry { Id = entryId };

        var revision = new JournalRevision
                       {
                               RevisionId = Guid.NewGuid().ToString("N")
                             , EntryId    = entryId
                             , CreatedUtc = DateTimeOffset.UtcNow
                             , Text       = text
                             , Mood       = mood
                             , MoodScore  = moodScore
                             , Tags       = tags ?? Array.Empty<string>()
                       };

        return new JournalEntryWithRevision(entry, revision, IsEdited: false);
    }
}
