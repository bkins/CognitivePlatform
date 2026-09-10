using CognitivePlatform.Api.Audit;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Controllers.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace CognitivePlatform.Tests;

public sealed class AdminTrustTraceControllerTests
{
    private const string AdminSecret = "test-admin-secret";

    [Fact]
    public void Get_Returns401_WhenAdminSecretIsMissing()
    {
        var controller = CreateController([]);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = controller.Get();

        var unauthorized = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public void Get_ReturnsOnlyTheSafeStoredTraceProjection()
    {
        var controller = CreateController(
        [
            new TrustTrace
            {
                    Id = "trace-1"
                  , ActionName = "tasks.create"
                  , Success = true
                  , Items = [new TransparencyItem("Safety gate", "Confirmation is required before execution.")]
            }
        ]);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Admin-Secret"] = AdminSecret;
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result);
        var trace = Assert.Single(Assert.IsAssignableFrom<IEnumerable<TrustTraceDto>>(ok.Value));
        Assert.Equal("trace-1", trace.Id);
        Assert.Equal("tasks.create", trace.ActionName);
        Assert.Equal("Safety gate", Assert.Single(trace.Items).Label);
    }

    private static AdminTrustTraceController CreateController(IReadOnlyList<TrustTrace> traces)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AdminSettings:AdminSecret"] = AdminSecret })
            .Build();
        var store = new Mock<ITrustTraceStore>();
        store.Setup(value => value.List(It.IsAny<int>())).Returns(traces);
        return new AdminTrustTraceController(configuration, store.Object);
    }
}
