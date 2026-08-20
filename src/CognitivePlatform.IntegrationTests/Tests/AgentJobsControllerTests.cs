using System.Net;
using System.Net.Http.Json;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Domains.Agent;
using CognitivePlatform.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class AgentJobsControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public AgentJobsControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task AgentJobs_FullLifecycle_Create_Start_Complete_RoundTrip()
    {
        _fixture.Log("Act — POST /api/agent/jobs to create a new job");
        var createPayload = new CreateAgentJobRequest
                            (
                                Prompt:         "Analyze weekly trends"
                              , ConversationId: "conv-123"
                              , Model:          "qwen2.5:14b"
                            );

        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/agent/jobs", createPayload);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var job = await _fixture.ReadJsonAsync<AgentJob>(createResponse);
        job.Should().NotBeNull();
        job!.Status.Should().Be(AgentJobStatus.Pending);
        var jobId = job.Id;

        _fixture.Log("Act — GET /api/agent/jobs/pending");
        var pendingResponse = await _fixture.Client.GetAsync("/api/agent/jobs/pending");
        pendingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pendingJobs = await _fixture.ReadJsonAsync<List<AgentJob>>(pendingResponse);
        pendingJobs.Should().Contain(item => item.Id == jobId);

        _fixture.Log($"Act — POST /api/agent/jobs/{jobId}/start");
        var startResponse = await _fixture.Client.PostAsync($"/api/agent/jobs/{jobId}/start", null);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var runningJob = await _fixture.ReadJsonAsync<AgentJob>(startResponse);
        runningJob!.Status.Should().Be(AgentJobStatus.Running);

        _fixture.Log($"Act — POST /api/agent/jobs/{jobId}/complete");
        var completePayload = new CompleteAgentJobRequest
                              (
                                  Response:       "Weekly trends analyzed successfully."
                                , ConversationId: "conv-123"
                              );

        var completeResponse = await _fixture.Client.PostAsJsonAsync($"/api/agent/jobs/{jobId}/complete", completePayload);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var completedJob = await _fixture.ReadJsonAsync<AgentJob>(completeResponse);
        completedJob!.Status.Should().Be(AgentJobStatus.Completed);
        completedJob.Response.Should().Be("Weekly trends analyzed successfully.");

        _fixture.Log($"Act — GET /api/agent/jobs/{jobId}");
        var getResponse = await _fixture.Client.GetAsync($"/api/agent/jobs/{jobId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetchedJob = await _fixture.ReadJsonAsync<AgentJob>(getResponse);
        fetchedJob!.Status.Should().Be(AgentJobStatus.Completed);
    }
}
