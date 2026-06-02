using CognitivePlatform.Api.Domains.BrainDump;
using Moq;

namespace CognitivePlatform.Tests;

public class BrainDumpActionsTests
{
    private readonly Mock<IBrainDumpService> _serviceMock = new();
    private readonly BrainDumpActions        _actions;

    public BrainDumpActionsTests()
    {
        _actions = new BrainDumpActions(_serviceMock.Object);
    }

    // -----------------------------------------------------------------------
    // StartBrainDump
    // -----------------------------------------------------------------------

    [Fact]
    public void StartBrainDump_ReturnsStringContainingSessionId()
    {
        var session = new BrainDumpSession { Id = "abc12345" };
        _serviceMock.Setup(service => service.StartSession()).Returns(session);

        var result = _actions.StartBrainDump();

        Assert.Contains("abc12345", result);
    }

    [Fact]
    public void StartBrainDump_ReturnsFirstCategoryPrompt()
    {
        var session = new BrainDumpSession { Id = "abc12345" };
        _serviceMock.Setup(service => service.StartSession()).Returns(session);

        var result = _actions.StartBrainDump();

        Assert.Contains("Things You're Putting Off", result);
    }

    [Fact]
    public void StartBrainDump_MentionsAvoidanceQuestions()
    {
        var session = new BrainDumpSession { Id = "x" };
        _serviceMock.Setup(service => service.StartSession()).Returns(session);

        var result = _actions.StartBrainDump();

        Assert.Contains("avoiding", result);
    }

    [Fact]
    public void StartBrainDump_CallsStartSession()
    {
        var session = new BrainDumpSession();
        _serviceMock.Setup(service => service.StartSession()).Returns(session);

        _actions.StartBrainDump();

        _serviceMock.Verify(service => service.StartSession(), Times.Once);
    }

    // -----------------------------------------------------------------------
    // GetLatestBrainDump
    // -----------------------------------------------------------------------

    [Fact]
    public void GetLatestBrainDump_ReturnsNoSessionMessage_WhenNoneExist()
    {
        _serviceMock.Setup(service => service.ListSessions(1))
                    .Returns(new List<BrainDumpSession>());

        var result = _actions.GetLatestBrainDump();

        Assert.Contains("No brain dump", result);
    }

    [Fact]
    public void GetLatestBrainDump_ReturnsSessionSummary_WhenSessionExists()
    {
        var session = new BrainDumpSession
                      {
                          Id        = "s1"
                        , Avoidance = "dentist appointment"
                        , Fears     = "returning to work"
                        , CreatedAt = DateTimeOffset.UtcNow
                      };
        _serviceMock.Setup(service => service.ListSessions(1))
                    .Returns(new List<BrainDumpSession> { session });

        var result = _actions.GetLatestBrainDump();

        Assert.Contains("dentist appointment", result);
        Assert.Contains("returning to work",   result);
    }

    [Fact]
    public void GetLatestBrainDump_IncludesExtractionSummary_WhenProcessed()
    {
        var session = new BrainDumpSession
                      {
                          Id                = "s1"
                        , Processed         = true
                        , ExtractionSummary = "Top themes: work stress, health"
                        , CreatedAt         = DateTimeOffset.UtcNow
                      };
        _serviceMock.Setup(service => service.ListSessions(1))
                    .Returns(new List<BrainDumpSession> { session });

        var result = _actions.GetLatestBrainDump();

        Assert.Contains("work stress", result);
    }

    // -----------------------------------------------------------------------
    // ListBrainDumps
    // -----------------------------------------------------------------------

    [Fact]
    public void ListBrainDumps_ReturnsNoSessionMessage_WhenNoneExist()
    {
        _serviceMock.Setup(service => service.ListSessions(10))
                    .Returns(new List<BrainDumpSession>());

        var result = _actions.ListBrainDumps();

        Assert.Contains("No brain dump", result);
    }

    [Fact]
    public void ListBrainDumps_ListsAllReturnedSessions()
    {
        var sessions = new List<BrainDumpSession>
                       {
                           new() { Id = "s1", CreatedAt = DateTimeOffset.UtcNow }
                         , new() { Id = "s2", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) }
                       };
        _serviceMock.Setup(service => service.ListSessions(10)).Returns(sessions);

        var result = _actions.ListBrainDumps();

        Assert.Contains("2 recent", result);
    }

    [Fact]
    public void ListBrainDumps_MarksProcessedSessions()
    {
        var sessions = new List<BrainDumpSession>
                       {
                           new() { Id = "s1", CreatedAt = DateTimeOffset.UtcNow, Processed = true }
                       };
        _serviceMock.Setup(service => service.ListSessions(10)).Returns(sessions);

        var result = _actions.ListBrainDumps();

        Assert.Contains("extracted", result);
    }
}
