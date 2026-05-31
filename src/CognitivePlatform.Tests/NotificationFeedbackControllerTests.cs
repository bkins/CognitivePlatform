using CognitivePlatform.Api.Controllers;
using CognitivePlatform.Api.Integrations.Notifications;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace CognitivePlatform.Tests;

public class NotificationFeedbackControllerTests
{
    private readonly Mock<INotificationScheduleProvider>  _providerMock = new();
    private readonly Mock<INotificationPatternService>    _patternMock  = new();

    private NotificationScheduleController MakeController()
        => new(_providerMock.Object, _patternMock.Object);

    public NotificationFeedbackControllerTests()
    {
        _patternMock.Setup(svc => svc.RecordFeedbackAsync(It.IsAny<string>()
                                                        , It.IsAny<NotificationFeedback>()
                                                        , It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
    }

    // ================================================================
    // Valid actions
    // ================================================================

    [Fact]
    public async Task PostFeedback_ReturnsNoContent_WhenActionIsTapped()
    {
        var result = await MakeController().PostFeedback(
            new NotificationFeedbackRequest { ExternalId = "day-open-2026-05-30", Action = "tapped" });

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task PostFeedback_ReturnsNoContent_WhenActionIsDismissed()
    {
        var result = await MakeController().PostFeedback(
            new NotificationFeedbackRequest { ExternalId = "journal-2026-05-30", Action = "dismissed" });

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task PostFeedback_ReturnsNoContent_WhenActionIsActed()
    {
        var result = await MakeController().PostFeedback(
            new NotificationFeedbackRequest { ExternalId = "wellbeing-checkin-2026-05-30", Action = "acted" });

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task PostFeedback_IsCaseInsensitive()
    {
        var result = await MakeController().PostFeedback(
            new NotificationFeedbackRequest { ExternalId = "day-open-2026-05-30", Action = "TAPPED" });

        Assert.IsType<NoContentResult>(result);
    }

    // ================================================================
    // Action → enum mapping
    // ================================================================

    [Fact]
    public async Task PostFeedback_CallsRecordFeedbackAsync_WithTapped()
    {
        await MakeController().PostFeedback(
            new NotificationFeedbackRequest { ExternalId = "day-open-2026-05-30", Action = "tapped" });

        _patternMock.Verify(svc => svc.RecordFeedbackAsync("day-open-2026-05-30"
                                                          , NotificationFeedback.Tapped
                                                          , It.IsAny<CancellationToken>())
                          , Times.Once);
    }

    [Fact]
    public async Task PostFeedback_CallsRecordFeedbackAsync_WithDismissed()
    {
        await MakeController().PostFeedback(
            new NotificationFeedbackRequest { ExternalId = "journal-2026-05-30", Action = "dismissed" });

        _patternMock.Verify(svc => svc.RecordFeedbackAsync("journal-2026-05-30"
                                                          , NotificationFeedback.Dismissed
                                                          , It.IsAny<CancellationToken>())
                          , Times.Once);
    }

    [Fact]
    public async Task PostFeedback_CallsRecordFeedbackAsync_WithActedUpon_ForActedAction()
    {
        await MakeController().PostFeedback(
            new NotificationFeedbackRequest { ExternalId = "wellbeing-checkin-2026-05-30", Action = "acted" });

        _patternMock.Verify(svc => svc.RecordFeedbackAsync("wellbeing-checkin-2026-05-30"
                                                          , NotificationFeedback.ActedUpon
                                                          , It.IsAny<CancellationToken>())
                          , Times.Once);
    }

    // ================================================================
    // Invalid actions
    // ================================================================

    [Fact]
    public async Task PostFeedback_ReturnsBadRequest_WhenActionIsUnknown()
    {
        var result = await MakeController().PostFeedback(
            new NotificationFeedbackRequest { ExternalId = "day-open-2026-05-30", Action = "explode" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task PostFeedback_ReturnsBadRequest_WhenActionIsEmpty()
    {
        var result = await MakeController().PostFeedback(
            new NotificationFeedbackRequest { ExternalId = "day-open-2026-05-30", Action = "" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task PostFeedback_DoesNotCallRecordFeedbackAsync_WhenActionIsInvalid()
    {
        await MakeController().PostFeedback(
            new NotificationFeedbackRequest { ExternalId = "day-open-2026-05-30", Action = "explode" });

        _patternMock.Verify(svc => svc.RecordFeedbackAsync(It.IsAny<string>()
                                                          , It.IsAny<NotificationFeedback>()
                                                          , It.IsAny<CancellationToken>())
                          , Times.Never);
    }
}
