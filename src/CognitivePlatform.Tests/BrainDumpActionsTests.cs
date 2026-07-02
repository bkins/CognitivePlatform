using CognitivePlatform.Api.Domains.BrainDump;
using Moq;

namespace CognitivePlatform.Tests;

public class BrainDumpActionsTests
{
    private readonly Mock<IBrainDumpService> _serviceMock = new();
    private readonly BrainDumpActions        _actions;

    public BrainDumpActionsTests()
    {
        _serviceMock.Setup(service => service.GetOrderedSessions())
                    .Returns(new List<(int Position, BrainDumpSession Session)>());
        _actions = new BrainDumpActions(_serviceMock.Object);
    }

    // -----------------------------------------------------------------------
    // StartBrainDump
    // -----------------------------------------------------------------------

    [Fact]
    public void StartBrainDump_ReturnsStringContainingSessionNumber()
    {
        var session = new BrainDumpSession { Id = "abc12345" };
        _serviceMock.Setup(service => service.StartSession()).Returns(session);
        _serviceMock.Setup(service => service.GetOrderedSessions())
                    .Returns(new List<(int Position, BrainDumpSession Session)> { (1, session) });

        var result = _actions.StartBrainDump();

        Assert.Contains("Session #1", result);
    }

    [Fact]
    public void StartBrainDump_ReturnsFirstCategoryPrompt()
    {
        var session = new BrainDumpSession { Id = "abc12345" };
        _serviceMock.Setup(service => service.StartSession()).Returns(session);
        _serviceMock.Setup(service => service.GetOrderedSessions())
                    .Returns(new List<(int Position, BrainDumpSession Session)> { (1, session) });

        var result = _actions.StartBrainDump();

        Assert.Contains("Things You're Putting Off", result);
    }

    [Fact]
    public void StartBrainDump_MentionsAvoidanceQuestions()
    {
        var session = new BrainDumpSession { Id = "x" };
        _serviceMock.Setup(service => service.StartSession()).Returns(session);
        _serviceMock.Setup(service => service.GetOrderedSessions())
                    .Returns(new List<(int Position, BrainDumpSession Session)> { (1, session) });

        var result = _actions.StartBrainDump();

        Assert.Contains("avoiding", result);
    }

    [Fact]
    public void StartBrainDump_CallsStartSession()
    {
        var session = new BrainDumpSession();
        _serviceMock.Setup(service => service.StartSession()).Returns(session);
        _serviceMock.Setup(service => service.GetOrderedSessions())
                    .Returns(new List<(int Position, BrainDumpSession Session)> { (1, session) });

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
        _serviceMock.Setup(service => service.GetOrderedSessions())
                    .Returns(new List<(int Position, BrainDumpSession Session)> { (1, session) });

        var result = _actions.GetLatestBrainDump();

        Assert.Contains("dentist appointment", result);
        Assert.Contains("returning to work",   result);
        Assert.Contains("Session #1",          result);
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
        _serviceMock.Setup(service => service.GetOrderedSessions())
                    .Returns(new List<(int Position, BrainDumpSession Session)> { (1, session) });

        var result = _actions.GetLatestBrainDump();

        Assert.Contains("work stress", result);
    }

    // -----------------------------------------------------------------------
    // ListBrainDumps
    // -----------------------------------------------------------------------

    [Fact]
    public void ListBrainDumps_ReturnsNoSessionMessage_WhenNoneExist()
    {
        _serviceMock.Setup(service => service.GetOrderedSessions())
                    .Returns(new List<(int Position, BrainDumpSession Session)>());

        var result = _actions.ListBrainDumps();

        Assert.Contains("No brain dump", result);
    }

    [Fact]
    public void ListBrainDumps_ListsAllReturnedSessions()
    {
        var sessions = new List<(int Position, BrainDumpSession Session)>
                       {
                           (1, new() { Id = "s1", CreatedAt = DateTimeOffset.UtcNow }),
                           (2, new() { Id = "s2", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) })
                       };
        _serviceMock.Setup(service => service.GetOrderedSessions()).Returns(sessions);

        var result = _actions.ListBrainDumps();

        Assert.Contains("2 recent", result);
        Assert.Contains("Session #1", result);
        Assert.Contains("Session #2", result);
    }

    [Fact]
    public void ListBrainDumps_MarksProcessedSessions()
    {
        var sessions = new List<(int Position, BrainDumpSession Session)>
                       {
                           (1, new() { Id = "s1", CreatedAt = DateTimeOffset.UtcNow, Processed = true })
                       };
        _serviceMock.Setup(service => service.GetOrderedSessions()).Returns(sessions);

        var result = _actions.ListBrainDumps();

        Assert.Contains("extracted", result);
        Assert.Contains("Session #1", result);
    }
}
