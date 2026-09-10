using CognitivePlatform.Api.Audit;
using CognitivePlatform.Api.Controllers.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace CognitivePlatform.Tests;

public sealed class AdminActionHistoryControllerTests
{
    private const string AdminSecret = "test-admin-secret";

    [Fact]
    public void GetHistory_Returns401_WhenAdminSecretIsMissing()
    {
        var controller = CreateController([]);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = controller.GetHistory();

        var unauthorized = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public void GetHistory_RedactsParametersAndFailureDetails()
    {
        var controller = CreateController(
        [
            new AuditEvent
            {
                ActionName = "calendar.create"
              , OccurredUtc = DateTimeOffset.Parse("2026-09-09T12:00:00Z")
              , Parameters = "title=Private appointment, token=secret-value"
              , Outcome = AuditOutcome.Failure
              , ErrorMessage = "Connection string leaked here"
            }
        ]);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Admin-Secret"] = AdminSecret;
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = controller.GetHistory();

        var ok = Assert.IsType<OkObjectResult>(result);
        var entry = Assert.Single(Assert.IsAssignableFrom<IEnumerable<ActionHistoryItemDto>>(ok.Value));
        Assert.Equal("Parameters redacted", entry.ParameterSummary);
        Assert.Equal("Execution reported a failure. See secured diagnostics for details.", entry.ExecutionSummary);
        Assert.DoesNotContain("Private appointment", entry.ParameterSummary);
        Assert.DoesNotContain("secret-value", entry.ParameterSummary);
        Assert.DoesNotContain("Connection string", entry.ExecutionSummary);
    }

    [Fact]
    public void GetHistory_FiltersByOutcomeAndClampsTake()
    {
        var controller = CreateController(
        [
            new AuditEvent { ActionName = "success", Outcome = AuditOutcome.Success },
            new AuditEvent { ActionName = "failure", Outcome = AuditOutcome.Failure }
        ]);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Admin-Secret"] = AdminSecret;
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = controller.GetHistory(take: 0, outcome: AuditOutcome.Failure);

        var ok = Assert.IsType<OkObjectResult>(result);
        var entry = Assert.Single(Assert.IsAssignableFrom<IEnumerable<ActionHistoryItemDto>>(ok.Value));
        Assert.Equal("failure", entry.ActionName);
    }

    private static AdminActionHistoryController CreateController(IReadOnlyList<AuditEvent> events)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AdminSettings:AdminSecret"] = AdminSecret })
            .Build();
        var auditLog = new Mock<IAuditLog>();
        auditLog.Setup(log => log.List(null, null)).Returns(events);
        return new AdminActionHistoryController(configuration, auditLog.Object);
    }
}
