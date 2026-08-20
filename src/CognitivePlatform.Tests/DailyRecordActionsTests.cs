using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Domains.Tasks;
using Moq;

namespace CognitivePlatform.Tests;

public class DailyRecordActionsTests
{
    private readonly Mock<IDailyRecordService> _dailyRecordServiceMock = new();
    private readonly Mock<ITaskService>        _taskServiceMock        = new();
    private readonly DailyRecordActions        _actions;

    public DailyRecordActionsTests()
    {
        _actions = new DailyRecordActions(_dailyRecordServiceMock.Object
                                        , _taskServiceMock.Object);
    }

    // BUG-28: CloseDay must not fail with MissingParameters when the user says
    // "close day" without providing any closing text. The action must accept a
    // null/empty closingText and substitute a sensible default before calling
    // the service.

    [Fact]
    public async Task CloseDay_WithNullClosingText_SucceedsWithDefaultText()
    {
        string? capturedText = null;

        var record = new DailyRecord { Id = "2026-05-10", Phase = DayPhase.Active };

        _dailyRecordServiceMock
            .Setup(service => service.CloseDayAsync( It.IsAny<string>()
                                                   , It.IsAny<string?>()
                                                   , It.IsAny<int?>()))
            .Callback<string, string?, int?>((text, _, _) => capturedText = text)
            .ReturnsAsync(record);

        await _actions.CloseDay(closingText: null);

        Assert.NotNull(capturedText);
        Assert.False(capturedText.HasNoValue()
                   , "Service must not receive null or empty closing text.");
    }

    [Fact]
    public async Task CloseDay_WithEmptyClosingText_SucceedsWithDefaultText()
    {
        string? capturedText = null;

        var record = new DailyRecord { Id = "2026-05-10", Phase = DayPhase.Active };

        _dailyRecordServiceMock
            .Setup(service => service.CloseDayAsync( It.IsAny<string>()
                                                   , It.IsAny<string?>()
                                                   , It.IsAny<int?>()))
            .Callback<string, string?, int?>((text, _, _) => capturedText = text)
            .ReturnsAsync(record);

        await _actions.CloseDay(closingText: "   ");

        Assert.NotNull(capturedText);
        Assert.False(capturedText.HasNoValue()
                   , "Service must not receive whitespace-only closing text.");
    }

    [Fact]
    public async Task CloseDay_WithProvidedClosingText_PassesTextThrough()
    {
        string? capturedText = null;

        var record = new DailyRecord { Id = "2026-05-10", Phase = DayPhase.Closed };

        _dailyRecordServiceMock
            .Setup(service => service.CloseDayAsync( It.IsAny<string>()
                                                   , It.IsAny<string?>()
                                                   , It.IsAny<int?>()))
            .Callback<string, string?, int?>((text, _, _) => capturedText = text)
            .ReturnsAsync(record);

        await _actions.CloseDay(closingText: "Solid day, finished the feature.");

        Assert.Equal("Solid day, finished the feature.", capturedText);
    }
}
