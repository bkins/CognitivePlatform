using CognitivePlatform.Api.Controllers.Admin;
using CognitivePlatform.Api.Governance;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace CognitivePlatform.Tests;

public class AdminCapabilityMaturityControllerTests
{
    [Fact]
    public async Task Propose_WithValidAdminSecret_ReturnsApprovedRecord()
    {
        var serviceMock = new Mock<ICapabilityMaturityService>();
        serviceMock.Setup(service => service.ProposeAsync("fixture-capability"
                                                        , It.IsAny<CapabilityMaturityEvidence>()
                                                        , "Ben"
                                                        , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new CapabilityMaturityRecord
                                 {
                                     CapabilityId   = "fixture-capability"
                                   , Level          = CapabilityMaturityLevel.Proposed
                                   , LastApprovedBy = "Ben"
                                 });
        var controller = CreateController(serviceMock.Object, authorized: true);

        var result = await controller.Propose("fixture-capability", new CapabilityMaturityApprovalRequest(CapabilityMaturityEvidence.ForProposal(), "Ben"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var record = Assert.IsType<CapabilityMaturityRecord>(ok.Value);
        Assert.Equal(CapabilityMaturityLevel.Proposed, record.Level);
    }

    [Fact]
    public async Task Quarantine_WithoutAdminSecret_ReturnsUnauthorized()
    {
        var controller = CreateController(Mock.Of<ICapabilityMaturityService>(), authorized: false);

        var result = await controller.Quarantine("fixture-capability", new CapabilityMaturityQuarantineRequest("Regression failed", "Ben"), CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, status.StatusCode);
    }

    private static AdminCapabilityMaturityController CreateController(ICapabilityMaturityService service, bool authorized)
    {
        var configuration = new ConfigurationBuilder()
                            .AddInMemoryCollection(new Dictionary<string, string?>
                                                   {
                                                       ["AdminSettings:AdminSecret"] = "test-secret"
                                                   })
                            .Build();
        var context = new DefaultHttpContext();
        if (authorized) context.Request.Headers["X-Admin-Secret"] = "test-secret";

        return new AdminCapabilityMaturityController(configuration, service)
               {
                   ControllerContext = new ControllerContext { HttpContext = context }
               };
    }
}
