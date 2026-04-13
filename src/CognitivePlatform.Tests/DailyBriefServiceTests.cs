using Moq;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Integrations.Calendar;

namespace CognitivePlatform.Tests;

public class DailyBriefServiceTests
{
    private readonly Mock<ITaskService>      _tasksMock    = new();
    private readonly Mock<ICalendarProvider> _calendarMock = new();
    private readonly DailyBriefService       _service;

    public DailyBriefServiceTests()
    {
        _service = new DailyBriefService(_tasksMock.Object
                                       , _calendarMock.Object);
    }

    // ================================================================
    // DO IT NOW (Important & Urgent)
    // ================================================================

    [Fact]
    public void GetBrief_IncludesDoItNowTask_WhenImportantAndUrgent()
    {
        _tasksMock.Setup(svc => svc.GetActive())
                  .Returns(new List<TaskItem>
                           {
                                   new()
                                   {
                                           ShortDescription = "Fix production bug"
                                         , IsImportant      = true
                                         , IsUrgent         = true
                                   }
                           });

        var result = _service.GetBrief();

        Assert.Contains("Fix production bug", result);
        Assert.Contains("Do It Now",          result);
    }

    [Fact]
    public void GetBrief_ShowsNoneForDoItNow_WhenNoImportantUrgentTasks()
    {
        _tasksMock.Setup(svc => svc.GetActive())
                  .Returns(new List<TaskItem>
                           {
                                   new()
                                   {
                                           ShortDescription = "Read a book"
                                         , IsImportant      = false
                                         , IsUrgent         = false
                                   }
                           });

        var result = _service.GetBrief();

        Assert.Contains("Do It Now", result);
        Assert.Contains("(none)", result);
    }

    // ================================================================
    // DUE TODAY / OVERDUE
    // ================================================================

    [Fact]
    public void GetBrief_IncludesDueTask_WhenDueDateIsToday()
    {
        var today = DateTimeOffset.UtcNow.Date;
        _tasksMock.Setup(svc => svc.GetActive())
                  .Returns(new List<TaskItem>
                           {
                                   new()
                                   {
                                           ShortDescription = "Submit invoice"
                                         , DueDate          = new DateTimeOffset(today, TimeSpan.Zero)
                                   }
                           });

        var result = _service.GetBrief();

        Assert.Contains("Submit invoice", result);
        Assert.Contains("Due Today",      result);
    }

    [Fact]
    public void GetBrief_IncludesOverdueLabel_WhenDueDateIsPast()
    {
        _tasksMock.Setup(svc => svc.GetActive())
                  .Returns(new List<TaskItem>
                           {
                                   new()
                                   {
                                           ShortDescription = "Pay bill"
                                         , DueDate          = DateTimeOffset.UtcNow.AddDays(-3)
                                   }
                           });

        var result = _service.GetBrief();

        Assert.Contains("Pay bill",  result);
        Assert.Contains("[OVERDUE]", result);
    }

    [Fact]
    public void GetBrief_DoesNotIncludeFutureTask_InDueTodaySection()
    {
        _tasksMock.Setup(svc => svc.GetActive())
                  .Returns(new List<TaskItem>
                           {
                                   new()
                                   {
                                           ShortDescription = "Future task"
                                         , DueDate          = DateTimeOffset.UtcNow.AddDays(5)
                                   }
                           });

        var result = _service.GetBrief();

        // Future task should not appear under Due Today, but Due Today section should show (none)
        var dueTodaySection = result.Substring(result.IndexOf("Due Today"
                                                            , StringComparison.Ordinal));
        Assert.Contains("(none)"
                      , dueTodaySection);
    }

    // ================================================================
    // STRUCTURE
    // ================================================================

    [Fact]
    public void GetBrief_AlwaysIncludesBothSections()
    {
        _tasksMock.Setup(svc => svc.GetActive())
                  .Returns(new List<TaskItem>());

        var result = _service.GetBrief();

        Assert.Contains("Do It Now",            result);
        Assert.Contains("Due Today or Overdue", result);
        Assert.Contains("Daily Brief",          result);
    }
}
