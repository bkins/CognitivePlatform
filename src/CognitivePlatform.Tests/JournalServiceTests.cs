using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Models;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Tests;

public class JournalServiceTests
{
    private readonly Mock<IObjectStore>               _storeMock          = new();
    private readonly Mock<IJournalRevisionRepository> _revisionRepoMock   = new();
    private readonly Mock<IJournalDraftRepository>    _draftRepoMock      = new();
    private readonly Mock<ILogger<JournalService>>    _loggerMock         = new();
    private readonly JournalService                   _service;

    public JournalServiceTests()
    {
        _storeMock.Setup(store => store.Save(It.IsAny<JournalEntry>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync(string.Empty);

        _storeMock.Setup(store => store.Save(It.IsAny<JournalRevision>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync(string.Empty);

        _draftRepoMock.Setup(repo => repo.AddAsync(It.IsAny<JournalDraft>()
                                                  , It.IsAny<CancellationToken>()))
                      .Returns(Task.CompletedTask);

        _service = new JournalService(_storeMock.Object
                                    , _revisionRepoMock.Object
                                    , _draftRepoMock.Object
                                    , _loggerMock.Object);
    }

    // ================================================================
    // ADD ENTRY ASYNC
    // ================================================================

    [Fact]
    public async Task AddEntryAsync_SavesEntryRevisionAndDraft_AndReturnsActualId()
    {
        var expectedId = Guid.NewGuid().ToString("N");
        _storeMock.Setup(store => store.Save(It.IsAny<JournalEntry>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync(expectedId);

        var result = await _service.AddEntryAsync("Great day."
                                                 , Array.Empty<string>()
                                                 , mood:       null
                                                 , moodScore:  null
                                                 , moodLevel:  null
                                                 , mediaPaths: Array.Empty<string>());

        Assert.Equal(expectedId, result);
        _storeMock.Verify(store => store.Save(It.IsAny<JournalRevision>()
                                            , It.IsAny<string?>()
                                            , It.IsAny<string?>())
                        , Times.Once);
        _draftRepoMock.Verify(repo => repo.AddAsync(It.IsAny<JournalDraft>()
                                                   , It.IsAny<CancellationToken>())
                            , Times.Once);
    }

    [Fact]
    public async Task AddEntryAsync_LogsWarning_WhenStoreReturnsIdDifferentFromGenerated()
    {
        _storeMock.Setup(store => store.Save(It.IsAny<JournalEntry>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync("a-different-id-entirely");

        await _service.AddEntryAsync("Some text."
                                   , Array.Empty<string>()
                                   , mood:       null
                                   , moodScore:  null
                                   , moodLevel:  null
                                   , mediaPaths: Array.Empty<string>());

        _loggerMock.Verify(logger => logger.Log(
                               LogLevel.Warning
                             , It.IsAny<EventId>()
                             , It.IsAny<It.IsAnyType>()
                             , It.IsAny<Exception?>()
                             , It.IsAny<Func<It.IsAnyType, Exception?, string>>())
                         , Times.Once);
    }

    // ================================================================
    // EDIT ENTRY
    // ================================================================

    [Fact]
    public void EditEntry_CreatesNewRevision_InheritingLatestFieldsWhenNotSupplied()
    {
        var entryId  = Guid.NewGuid().ToString("N");
        var entry    = MakeEntry(entryId);
        var original = MakeRevision(entryId, "Original text", new[] { "tag1" }, "Happy", 4);

        _storeMock.Setup(store => store.Get<JournalEntry>(entryId, null)).Returns(entry);
        _revisionRepoMock.Setup(repo => repo.GetRevisionsByEntryId(entryId))
                         .Returns(new List<JournalRevision> { original });

        var result = _service.EditEntry(entryId, text: "Updated text");

        Assert.Equal("Updated text",        result.Text);
        Assert.Equal(original.Tags,         result.Tags);
        Assert.Equal(original.Mood,         result.Mood);
        Assert.Equal(original.MoodScore,    result.MoodScore);
        Assert.Equal(entryId,               result.EntryId);
    }

    [Fact]
    public void EditEntry_Throws_KeyNotFoundException_WhenEntryDoesNotExist()
    {
        var entryId = Guid.NewGuid().ToString("N");

        _storeMock.Setup(store => store.Get<JournalEntry>(entryId, null))
                  .Returns((JournalEntry?)null);

        Assert.Throws<KeyNotFoundException>(() => _service.EditEntry(entryId, text: "x"));
    }

    [Fact]
    public void EditEntry_Throws_InvalidOperationException_WhenEntrySoftDeleted()
    {
        var entryId = Guid.NewGuid().ToString("N");
        var entry   = MakeEntry(entryId, deletedUtc: DateTimeOffset.UtcNow);

        _storeMock.Setup(store => store.Get<JournalEntry>(entryId, null)).Returns(entry);

        Assert.Throws<InvalidOperationException>(() => _service.EditEntry(entryId, text: "x"));
    }

    // BUG-05: null could not distinguish "don't change" from "explicitly clear".
    // clear* flags resolve the ambiguity without changing the default no-op behaviour.
    [Fact]
    public void EditEntry_ClearsTags_WhenClearTagsIsTrue()
    {
        var entryId  = Guid.NewGuid().ToString("N");
        var entry    = MakeEntry(entryId);
        var original = MakeRevision(entryId, "Text", new[] { "tag1", "tag2" }, "Happy", 4);

        _storeMock.Setup(store => store.Get<JournalEntry>(entryId, null)).Returns(entry);
        _revisionRepoMock.Setup(repo => repo.GetRevisionsByEntryId(entryId))
                         .Returns(new List<JournalRevision> { original });

        var result = _service.EditEntry(entryId, clearTags: true);

        Assert.Empty(result.Tags);
    }

    [Fact]
    public void EditEntry_ClearsMood_WhenClearMoodIsTrue()
    {
        var entryId  = Guid.NewGuid().ToString("N");
        var entry    = MakeEntry(entryId);
        var original = MakeRevision(entryId, "Text", mood: "Happy");

        _storeMock.Setup(store => store.Get<JournalEntry>(entryId, null)).Returns(entry);
        _revisionRepoMock.Setup(repo => repo.GetRevisionsByEntryId(entryId))
                         .Returns(new List<JournalRevision> { original });

        var result = _service.EditEntry(entryId, clearMood: true);

        Assert.Null(result.Mood);
    }

    [Fact]
    public void EditEntry_ClearsMoodScore_WhenClearMoodScoreIsTrue()
    {
        var entryId  = Guid.NewGuid().ToString("N");
        var entry    = MakeEntry(entryId);
        var original = MakeRevision(entryId, "Text", moodScore: 4);

        _storeMock.Setup(store => store.Get<JournalEntry>(entryId, null)).Returns(entry);
        _revisionRepoMock.Setup(repo => repo.GetRevisionsByEntryId(entryId))
                         .Returns(new List<JournalRevision> { original });

        var result = _service.EditEntry(entryId, clearMoodScore: true);

        Assert.Null(result.MoodScore);
    }

    [Fact]
    public void EditEntry_Throws_InvalidOperationException_WhenEntryHasNoRevisions()
    {
        var entryId = Guid.NewGuid().ToString("N");
        var entry   = MakeEntry(entryId);

        _storeMock.Setup(store => store.Get<JournalEntry>(entryId, null)).Returns(entry);
        _revisionRepoMock.Setup(repo => repo.GetRevisionsByEntryId(entryId))
                         .Returns(new List<JournalRevision>());

        Assert.Throws<InvalidOperationException>(() => _service.EditEntry(entryId, text: "x"));
    }

    // ================================================================
    // GET ENTRY
    // ================================================================

    [Fact]
    public void GetEntry_Throws_ArgumentException_WhenIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => _service.GetEntry(string.Empty));
    }

    [Fact]
    public void GetEntry_Throws_ArgumentException_WhenIdIsWhitespace()
    {
        Assert.Throws<ArgumentException>(() => _service.GetEntry("   "));
    }

    [Fact]
    public void GetEntry_ReturnsNull_WhenEntryNotFoundInStore()
    {
        _storeMock.Setup(store => store.Get<JournalEntry>("missing", null))
                  .Returns((JournalEntry?)null);

        var result = _service.GetEntry("missing");

        Assert.Null(result);
    }

    [Fact]
    public void GetEntry_ReturnsEntry_WhenFound()
    {
        var id    = Guid.NewGuid().ToString("N");
        var entry = MakeEntry(id);

        _storeMock.Setup(store => store.Get<JournalEntry>(id, null)).Returns(entry);

        var result = _service.GetEntry(id);

        Assert.Equal(entry, result);
    }

    // ================================================================
    // GET BY ID
    // ================================================================

    [Fact]
    public void GetById_Throws_KeyNotFoundException_WhenEntryNotFound()
    {
        var id = Guid.NewGuid().ToString("N");

        _storeMock.Setup(store => store.Get<JournalEntry>(id, null))
                  .Returns((JournalEntry?)null);

        Assert.Throws<KeyNotFoundException>(() => _service.GetById(id));
    }

    [Fact]
    public void GetById_Throws_InvalidOperationException_WhenNoRevisions()
    {
        var id    = Guid.NewGuid().ToString("N");
        var entry = MakeEntry(id);

        _storeMock.Setup(store => store.Get<JournalEntry>(id, null)).Returns(entry);
        _revisionRepoMock.Setup(repo => repo.GetRevisionsByEntryId(id))
                         .Returns(new List<JournalRevision>());

        Assert.Throws<InvalidOperationException>(() => _service.GetById(id));
    }

    [Fact]
    public void GetById_Returns_IsEdited_False_WhenOneRevision()
    {
        var id       = Guid.NewGuid().ToString("N");
        var entry    = MakeEntry(id);
        var revision = MakeRevision(id, "First and only.");

        _storeMock.Setup(store => store.Get<JournalEntry>(id, null)).Returns(entry);
        _revisionRepoMock.Setup(repo => repo.GetRevisionsByEntryId(id))
                         .Returns(new List<JournalRevision> { revision });

        var result = _service.GetById(id);

        Assert.False(result.IsEdited);
        Assert.Equal(revision, result.LatestRevision);
    }

    [Fact]
    public void GetById_Returns_IsEdited_True_WhenMoreThanOneRevision()
    {
        var id        = Guid.NewGuid().ToString("N");
        var entry     = MakeEntry(id);
        var revisionA = MakeRevision(id, "First.");
        var revisionB = MakeRevision(id, "Second.");

        _storeMock.Setup(store => store.Get<JournalEntry>(id, null)).Returns(entry);
        _revisionRepoMock.Setup(repo => repo.GetRevisionsByEntryId(id))
                         .Returns(new List<JournalRevision> { revisionB, revisionA }); // newest first

        var result = _service.GetById(id);

        Assert.True(result.IsEdited);
        Assert.Equal(revisionB, result.LatestRevision);
    }

    // ================================================================
    // LIST ENTRIES
    // ================================================================

    [Fact]
    public void ListEntries_ReturnsEntriesOrderedByCreatedUtcAscending()
    {
        var earlier = MakeEntry(Guid.NewGuid().ToString("N"), createdUtc: DateTimeOffset.UtcNow.AddDays(-2));
        var later   = MakeEntry(Guid.NewGuid().ToString("N"), createdUtc: DateTimeOffset.UtcNow.AddDays(-1));
        var rev1    = MakeRevision(earlier.Id, "Earlier.");
        var rev2    = MakeRevision(later.Id,   "Later.");

        _storeMock.Setup(store => store.List<JournalEntry>(null, null, null))
                  .Returns(new List<JournalEntry> { later, earlier }); // reversed
        _revisionRepoMock.Setup(repo => repo.GetRevisionsByEntryId(earlier.Id))
                         .Returns(new List<JournalRevision> { rev1 });
        _revisionRepoMock.Setup(repo => repo.GetRevisionsByEntryId(later.Id))
                         .Returns(new List<JournalRevision> { rev2 });

        var results = _service.ListEntries();

        Assert.Equal(2,           results.Count);
        Assert.Equal(earlier.Id,  results[0].Entry.Id);
        Assert.Equal(later.Id,    results[1].Entry.Id);
    }

    [Fact]
    public void ListEntries_ExcludesSoftDeletedEntries()
    {
        var active  = MakeEntry(Guid.NewGuid().ToString("N"));
        var deleted = MakeEntry(Guid.NewGuid().ToString("N"), deletedUtc: DateTimeOffset.UtcNow);
        var revA    = MakeRevision(active.Id,  "Active.");
        var revD    = MakeRevision(deleted.Id, "Deleted.");

        _storeMock.Setup(store => store.List<JournalEntry>(null, null, null))
                  .Returns(new List<JournalEntry> { active, deleted });
        _revisionRepoMock.Setup(repo => repo.GetRevisionsByEntryId(active.Id))
                         .Returns(new List<JournalRevision> { revA });
        _revisionRepoMock.Setup(repo => repo.GetRevisionsByEntryId(deleted.Id))
                         .Returns(new List<JournalRevision> { revD });

        var results = _service.ListEntries();

        Assert.Single(results);
        Assert.Equal(active.Id, results[0].Entry.Id);
    }

    [Fact]
    public void ListEntries_ExcludesEntriesWithNoRevisions()
    {
        var withRevision    = MakeEntry(Guid.NewGuid().ToString("N"));
        var withoutRevision = MakeEntry(Guid.NewGuid().ToString("N"));
        var rev             = MakeRevision(withRevision.Id, "Has text.");

        _storeMock.Setup(store => store.List<JournalEntry>(null, null, null))
                  .Returns(new List<JournalEntry> { withRevision, withoutRevision });
        _revisionRepoMock.Setup(repo => repo.GetRevisionsByEntryId(withRevision.Id))
                         .Returns(new List<JournalRevision> { rev });
        _revisionRepoMock.Setup(repo => repo.GetRevisionsByEntryId(withoutRevision.Id))
                         .Returns(new List<JournalRevision>());

        var results = _service.ListEntries();

        Assert.Single(results);
        Assert.Equal(withRevision.Id, results[0].Entry.Id);
    }

    [Fact]
    public void ListEntries_PassesDateRangeToStore()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-7);
        var to   = DateTimeOffset.UtcNow;

        _storeMock.Setup(store => store.List<JournalEntry>(null, from, to))
                  .Returns(new List<JournalEntry>());

        _service.ListEntries(fromUtc: from, toUtc: to);

        _storeMock.Verify(store => store.List<JournalEntry>(null, from, to), Times.Once);
    }

    // ================================================================
    // DELETE ENTRY
    // ================================================================

    [Fact]
    public void DeleteEntry_ReturnsFalse_WhenEntryNotFound()
    {
        _storeMock.Setup(store => store.Get<JournalEntry>("missing", null))
                  .Returns((JournalEntry?)null);

        var result = _service.DeleteEntry("missing", "test reason");

        Assert.False(result);
    }

    [Fact]
    public void DeleteEntry_Throws_InvalidOperationException_WhenAlreadyDeleted()
    {
        var id    = Guid.NewGuid().ToString("N");
        var entry = MakeEntry(id, deletedUtc: DateTimeOffset.UtcNow);

        _storeMock.Setup(store => store.Get<JournalEntry>(id, null)).Returns(entry);

        Assert.Throws<InvalidOperationException>(() => _service.DeleteEntry(id, "double delete"));
    }

    [Fact]
    public void DeleteEntry_SetsDeletedUtcAndReason_WithoutHardDelete()
    {
        var id    = Guid.NewGuid().ToString("N");
        var entry = MakeEntry(id);

        _storeMock.Setup(store => store.Get<JournalEntry>(id, null)).Returns(entry);

        var result = _service.DeleteEntry(id, "no longer needed");

        Assert.True(result);
        Assert.NotNull(entry.DeletedUtc);
        Assert.Equal("no longer needed", entry.DeletedReason);
        _storeMock.Verify(store => store.Save(entry, It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
    }

    // ================================================================
    // EXISTS
    // ================================================================

    [Fact]
    public void Exists_ReturnsTrue_WhenStoreReturnsEntry()
    {
        var id    = Guid.NewGuid();
        var entry = MakeEntry(id.ToString("N"));

        _storeMock.Setup(store => store.Get<JournalEntry>(id.ToString("N"), null)).Returns(entry);

        var result = _service.Exists(id);

        Assert.True(result);
    }

    [Fact]
    public void Exists_ReturnsFalse_WhenStoreReturnsNull()
    {
        var id = Guid.NewGuid();

        _storeMock.Setup(store => store.Get<JournalEntry>(id.ToString("N"), null))
                  .Returns((JournalEntry?)null);

        var result = _service.Exists(id);

        Assert.False(result);
    }

    // ================================================================
    // LIST ENTRIES ON THIS DAY
    // ================================================================

    [Fact]
    public void ListEntriesOnThisDay_ReturnsOnlyMatchingMonthAndDay()
    {
        var target = new JournalEntry
                     {
                             Id         = Guid.NewGuid().ToString("N")
                           , CreatedUtc = new DateTimeOffset(2024, 3, 15, 10, 0, 0, TimeSpan.Zero)
                     };
        var other  = new JournalEntry
                     {
                             Id         = Guid.NewGuid().ToString("N")
                           , CreatedUtc = new DateTimeOffset(2024, 4, 20, 10, 0, 0, TimeSpan.Zero)
                     };

        _storeMock.Setup(store => store.List<JournalEntry>(nameof(JournalEntry), null, null))
                  .Returns(new List<JournalEntry> { target, other });

        var results = _service.ListEntriesOnThisDay(month: 3, day: 15);

        Assert.Single(results);
        Assert.Equal(target.Id, results[0].Id);
    }

    [Fact]
    public void ListEntriesOnThisDay_ExcludesSoftDeletedEntries()
    {
        var active  = new JournalEntry
                      {
                              Id         = Guid.NewGuid().ToString("N")
                            , CreatedUtc = new DateTimeOffset(2024, 6, 1, 10, 0, 0, TimeSpan.Zero)
                      };
        var deleted = new JournalEntry
                      {
                              Id         = Guid.NewGuid().ToString("N")
                            , CreatedUtc = new DateTimeOffset(2024, 6, 1, 11, 0, 0, TimeSpan.Zero)
                            , DeletedUtc = DateTimeOffset.UtcNow
                      };

        _storeMock.Setup(store => store.List<JournalEntry>(nameof(JournalEntry), null, null))
                  .Returns(new List<JournalEntry> { active, deleted });

        var results = _service.ListEntriesOnThisDay(month: 6, day: 1);

        Assert.Single(results);
        Assert.Equal(active.Id, results[0].Id);
    }

    [Fact]
    public void ListEntriesOnThisDay_ReturnsResultsOrderedByCreatedUtcDescending()
    {
        var older = new JournalEntry
                    {
                            Id         = Guid.NewGuid().ToString("N")
                          , CreatedUtc = new DateTimeOffset(2024, 9, 10, 8,  0, 0, TimeSpan.Zero)
                    };
        var newer = new JournalEntry
                    {
                            Id         = Guid.NewGuid().ToString("N")
                          , CreatedUtc = new DateTimeOffset(2024, 9, 10, 12, 0, 0, TimeSpan.Zero)
                    };

        _storeMock.Setup(store => store.List<JournalEntry>(nameof(JournalEntry), null, null))
                  .Returns(new List<JournalEntry> { older, newer });

        var results = _service.ListEntriesOnThisDay(month: 9, day: 10);

        Assert.Equal(2,        results.Count);
        Assert.Equal(newer.Id, results[0].Id);
        Assert.Equal(older.Id, results[1].Id);
    }

    // ================================================================
    // MAP MOOD LEVEL (static — no mocks)
    // ================================================================

    [Theory]
    [InlineData(0,  MoodLevel.VeryNegative)]
    [InlineData(1,  MoodLevel.VeryNegative)]
    [InlineData(-5, MoodLevel.VeryNegative)]
    public void MapMoodLevel_ReturnsVeryNegative_ForScoresAtOrBelowOne(int score, MoodLevel expected)
    {
        Assert.Equal(expected, JournalService.MapMoodLevel(score));
    }

    [Fact]
    public void MapMoodLevel_ReturnsNegative_ForScoreOfTwo()
    {
        Assert.Equal(MoodLevel.Negative, JournalService.MapMoodLevel(2));
    }

    [Fact]
    public void MapMoodLevel_ReturnsNeutral_ForScoreOfThree()
    {
        Assert.Equal(MoodLevel.Neutral, JournalService.MapMoodLevel(3));
    }

    [Fact]
    public void MapMoodLevel_ReturnsPositive_ForScoreOfFour()
    {
        Assert.Equal(MoodLevel.Positive, JournalService.MapMoodLevel(4));
    }

    [Theory]
    [InlineData(5,  MoodLevel.VeryPositive)]
    [InlineData(6,  MoodLevel.VeryPositive)]
    [InlineData(10, MoodLevel.VeryPositive)]
    public void MapMoodLevel_ReturnsVeryPositive_ForScoresAtOrAboveFive(int score, MoodLevel expected)
    {
        Assert.Equal(expected, JournalService.MapMoodLevel(score));
    }

    // ================================================================
    // MAP MOOD EMOJI (static — no mocks)
    // ================================================================

    [Theory]
    [InlineData(MoodLevel.VeryNegative, "😢")]
    [InlineData(MoodLevel.Negative,     "🙁")]
    [InlineData(MoodLevel.Neutral,      "😐")]
    [InlineData(MoodLevel.Positive,     "🙂")]
    [InlineData(MoodLevel.VeryPositive, "😄")]
    public void MapMoodEmoji_ReturnsCorrectEmoji_ForEachMoodLevel(MoodLevel level, string expected)
    {
        Assert.Equal(expected, JournalService.MapMoodEmoji(level));
    }

    [Fact]
    public void MapMoodEmoji_ReturnsFallback_ForUnknownMoodLevel()
    {
        Assert.Equal("❓", JournalService.MapMoodEmoji((MoodLevel)99));
    }

    // ================================================================
    // HELPERS
    // ================================================================

    private static JournalEntry MakeEntry( string          id
                                         , DateTimeOffset?  createdUtc = null
                                         , DateTimeOffset?  deletedUtc = null)
        => new()
           {
                   Id         = id
                 , CreatedUtc = createdUtc ?? DateTimeOffset.UtcNow
                 , DeletedUtc = deletedUtc
           };

    private static JournalRevision MakeRevision( string    entryId
                                               , string    text
                                               , string[]? tags      = null
                                               , string?   mood      = null
                                               , int?      moodScore = null)
        => new()
           {
                   RevisionId = Guid.NewGuid().ToString("N")
                 , EntryId    = entryId
                 , CreatedUtc = DateTimeOffset.UtcNow
                 , Text       = text
                 , Tags       = tags ?? Array.Empty<string>()
                 , Mood       = mood
                 , MoodScore  = moodScore
           };
}
