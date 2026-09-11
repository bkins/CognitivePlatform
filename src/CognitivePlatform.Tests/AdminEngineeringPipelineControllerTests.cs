using CognitivePlatform.Api.Automation;
using CognitivePlatform.Api.Controllers.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace CognitivePlatform.Tests;

public class AdminEngineeringPipelineControllerTests
{
    [Fact]
    public async Task Create_WithValidAdminSecret_ReturnsPlannedEngineeringRun()
    {
        var serviceMock = new Mock<IEngineeringPipelineService>();
        serviceMock.Setup(service => service.CreateAsync(It.IsAny<CreateEngineeringRunRequest>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new EngineeringRecord
                                 {
                                     Id           = "run-id"
                                   , CapabilityId = "fixture-capability"
                                   , Status       = EngineeringRunStatus.Planned
                                 });
        var controller = CreateController(serviceMock.Object, authorized: true);

        var result = await controller.Create(new CreateEngineeringRunRequest("fixture-capability", "Add a fixture", "abc123"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var record = Assert.IsType<EngineeringRecord>(ok.Value);
        Assert.Equal(EngineeringRunStatus.Planned, record.Status);
    }

    [Fact]
    public async Task ProvisionWorkspace_WithoutAdminSecret_ReturnsUnauthorized()
    {
        var controller = CreateController(Mock.Of<IEngineeringPipelineService>(), authorized: false);

        var result = await controller.ProvisionWorkspace("run-id", CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, status.StatusCode);
    }

    private static AdminEngineeringPipelineController CreateController(IEngineeringPipelineService service, bool authorized)
    {
        var configuration = new ConfigurationBuilder()
                            .AddInMemoryCollection(new Dictionary<string, string?>
                                                   {
                                                       ["AdminSettings:AdminSecret"] = "test-secret"
                                                   })
                            .Build();
        var context = new DefaultHttpContext();
        if (authorized) context.Request.Headers["X-Admin-Secret"] = "test-secret";

        return new AdminEngineeringPipelineController(configuration, service)
               {
                   ControllerContext = new ControllerContext { HttpContext = context }
               };
    }
}
