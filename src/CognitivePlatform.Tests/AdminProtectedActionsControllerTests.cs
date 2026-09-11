using CognitivePlatform.Api.Automation;
using CognitivePlatform.Api.Controllers.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace CognitivePlatform.Tests;

public class AdminProtectedActionsControllerTests
{
    [Fact]
    public async Task Request_WithValidAdminSecret_ReturnsAwaitingApprovalRecord()
    {
        var serviceMock = new Mock<IProtectedActionApprovalService>();
        serviceMock.Setup(service => service.RequestAsync(It.IsAny<ProtectedActionRequest>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new ProtectedActionRequestRecord
                                 {
                                     Id          = "protected-action-id"
                                   , IsProtected = true
                                   , Status      = ProtectedActionStatus.AwaitingApproval
                                 });
        var controller = CreateController(serviceMock.Object, authorized: true);

        var result = await controller.Create(new ProtectedActionRequest("run-id", "deploy", EngineeringActionKind.Deployment), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var record = Assert.IsType<ProtectedActionRequestRecord>(ok.Value);
        Assert.Equal(ProtectedActionStatus.AwaitingApproval, record.Status);
    }

    [Fact]
    public async Task Approve_WithoutAdminSecret_ReturnsUnauthorized()
    {
        var controller = CreateController(Mock.Of<IProtectedActionApprovalService>(), authorized: false);

        var result = await controller.Approve("protected-action-id", new ProtectedActionApprovalRequest("Ben", "Approved", "Rollback plan"), CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, status.StatusCode);
    }

    private static AdminProtectedActionsController CreateController(IProtectedActionApprovalService service, bool authorized)
    {
        var configuration = new ConfigurationBuilder()
                            .AddInMemoryCollection(new Dictionary<string, string?>
                                                   {
                                                       ["AdminSettings:AdminSecret"] = "test-secret"
                                                   })
                            .Build();
        var context = new DefaultHttpContext();
        if (authorized) context.Request.Headers["X-Admin-Secret"] = "test-secret";

        return new AdminProtectedActionsController(configuration, service)
               {
                   ControllerContext = new ControllerContext { HttpContext = context }
               };
    }
}
