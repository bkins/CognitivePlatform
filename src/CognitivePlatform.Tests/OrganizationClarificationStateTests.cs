using CognitivePlatform.Api.Controllers;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Orchestrator;
using CognitivePlatform.Api.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CognitivePlatform.Tests;

public class OrganizationClarificationStateTests
{
    [Fact]
    public void State_Follows_Pending_Intent_Reset_And_Session_Isolation()
    {
        var contexts = new ConversationContextStore();
        var services = new ServiceCollection().AddSingleton(contexts).BuildServiceProvider();
        var controller = ActivatorUtilities.CreateInstance<ConversationController>(services,
            Mock.Of<IConversationOrchestrator>(), Mock.Of<ITelemetrySink>(), new TelemetryContext { SessionId = "state-test" },
            Mock.Of<IConversationTurnStore>(), Mock.Of<IConversationMetadataStore>());
        var endpoint = Assert.Single(typeof(ConversationController).GetMethods(), method => method.GetCustomAttributes(typeof(HttpGetAttribute), true)
                .Cast<HttpGetAttribute>().Any(attribute => attribute.Template == "{id}/organization-clarification-pending"));

        Assert.False(Assert.IsType<bool>(endpoint.Invoke(controller, ["first"])));
        contexts.GetOrCreate("first").PendingOrganizationIntent = new PendingOrganizationIntent { OriginalInput = "Help me get organized" };
        Assert.True(Assert.IsType<bool>(endpoint.Invoke(controller, ["first"])));
        Assert.False(Assert.IsType<bool>(endpoint.Invoke(controller, ["other"])));
        contexts.GetOrCreate("first").Reset();
        Assert.False(Assert.IsType<bool>(endpoint.Invoke(controller, ["first"])));
    }
}
