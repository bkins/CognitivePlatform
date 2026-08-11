using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Controllers;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Agent;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public class AgentJobsControllerTests
{
    private readonly Mock<IObjectStore>     _storeMock = new();
    private readonly AgentJobsController    _sut;

    public AgentJobsControllerTests()
    {
        _sut = new AgentJobsController(_storeMock.Object);
    }

    [Fact]
    public async Task CreateJob_WithValidRequest_ReturnsOkAndSavesJob()
    {
        var request = new CreateAgentJobRequest("Test prompt", "conv-123");

        _storeMock
            .Setup(store => store.Save(It.IsAny<AgentJob>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("resolved-id");

        var result = await _sut.CreateJob(request);

        var ok      = Assert.IsType<OkObjectResult>(result);
        var created = Assert.IsType<AgentJob>(ok.Value);
        Assert.Equal("Test prompt", created.Prompt);
        Assert.Equal("conv-123",    created.ConversationId);
        Assert.Equal(AgentJobStatus.Pending, created.Status);
        _storeMock.Verify(store => store.Save(It.IsAny<AgentJob>(), "AgentJobs", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task GetPendingJobs_ReturnsOnlyPendingJobs()
    {
        var jobs = new List<AgentJob>
                   {
                       new() { Id = "job-1", Status = AgentJobStatus.Pending }
                     , new() { Id = "job-2", Status = AgentJobStatus.Running }
                     , new() { Id = "job-3", Status = AgentJobStatus.Completed }
                     , new() { Id = "job-4", Status = AgentJobStatus.Pending }
                   };

        _storeMock
            .Setup(store => store.ListAsync<AgentJob>(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        var result = await _sut.GetPendingJobs();

        var ok      = Assert.IsType<OkObjectResult>(result);
        var pending = Assert.IsAssignableFrom<IEnumerable<AgentJob>>(ok.Value).ToList();
        Assert.Equal(2, pending.Count);
        Assert.Contains(pending, job => job.Id == "job-1");
        Assert.Contains(pending, job => job.Id == "job-4");
    }

    [Fact]
    public async Task GetRunningJobs_ReturnsOnlyRunningJobs()
    {
        var jobs = new List<AgentJob>
                   {
                       new() { Id = "job-1", Status = AgentJobStatus.Pending }
                     , new() { Id = "job-2", Status = AgentJobStatus.Running }
                     , new() { Id = "job-3", Status = AgentJobStatus.Completed }
                     , new() { Id = "job-4", Status = AgentJobStatus.Running }
                   };

        _storeMock
            .Setup(store => store.ListAsync<AgentJob>(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        var result = await _sut.GetRunningJobs();

        var ok      = Assert.IsType<OkObjectResult>(result);
        var running = Assert.IsAssignableFrom<IEnumerable<AgentJob>>(ok.Value).ToList();
        Assert.Equal(2, running.Count);
        Assert.Contains(running, job => job.Id == "job-2");
        Assert.Contains(running, job => job.Id == "job-4");
    }

    [Fact]
    public async Task StartJob_WhenJobExists_UpdatesStatusAndStartedUtcAndReturnsOk()
    {
        var job = new AgentJob { Id = "job-123", Status = AgentJobStatus.Pending };

        _storeMock
            .Setup(store => store.GetAsync<AgentJob>("job-123", "AgentJobs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var result = await _sut.StartJob("job-123");

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(AgentJobStatus.Running, job.Status);
        Assert.NotNull(job.StartedUtc);
        _storeMock.Verify(store => store.Save(job, "AgentJobs", "job-123"), Times.Once);
    }

    [Fact]
    public async Task StartJob_WhenJobDoesNotExist_ReturnsNotFound()
    {
        _storeMock
            .Setup(store => store.GetAsync<AgentJob>("missing-job", "AgentJobs", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentJob?)null);

        var result = await _sut.StartJob("missing-job");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task CompleteJob_WhenJobExists_UpdatesStatusAndCompletedUtcAndReturnsOk()
    {
        var job = new AgentJob { Id = "job-123", Status = AgentJobStatus.Running };
        var request = new CompleteAgentJobRequest("Success response", "conv-new-789");

        _storeMock
            .Setup(store => store.GetAsync<AgentJob>("job-123", "AgentJobs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var result = await _sut.CompleteJob("job-123", request);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(AgentJobStatus.Completed, job.Status);
        Assert.Equal("Success response",       job.Response);
        Assert.Equal("conv-new-789",           job.ConversationId);
        Assert.NotNull(job.CompletedUtc);
        _storeMock.Verify(store => store.Save(job, "AgentJobs", "job-123"), Times.Once);
    }

    [Fact]
    public async Task CompleteJob_WhenJobDoesNotExist_ReturnsNotFound()
    {
        var request = new CompleteAgentJobRequest("Success response", null);

        _storeMock
            .Setup(store => store.GetAsync<AgentJob>("missing-job", "AgentJobs", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentJob?)null);

        var result = await _sut.CompleteJob("missing-job", request);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task FailJob_WhenJobExists_UpdatesStatusAndCompletedUtcAndErrorAndReturnsOk()
    {
        var job = new AgentJob { Id = "job-123", Status = AgentJobStatus.Running };
        var request = new FailAgentJobRequest("Failed to compile code");

        _storeMock
            .Setup(store => store.GetAsync<AgentJob>("job-123", "AgentJobs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var result = await _sut.FailJob("job-123", request);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(AgentJobStatus.Failed,     job.Status);
        Assert.Equal("Failed to compile code", job.Error);
        Assert.NotNull(job.CompletedUtc);
        _storeMock.Verify(store => store.Save(job, "AgentJobs", "job-123"), Times.Once);
    }

    [Fact]
    public async Task FailJob_WhenJobDoesNotExist_ReturnsNotFound()
    {
        var request = new FailAgentJobRequest("Error");

        _storeMock
            .Setup(store => store.GetAsync<AgentJob>("missing-job", "AgentJobs", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentJob?)null);

        var result = await _sut.FailJob("missing-job", request);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetJob_WhenJobExists_ReturnsJob()
    {
        var job = new AgentJob { Id = "job-777", Prompt = "Help me" };

        _storeMock
            .Setup(store => store.GetAsync<AgentJob>("job-777", "AgentJobs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var result = await _sut.GetJob("job-777");

        var ok    = Assert.IsType<OkObjectResult>(result);
        var value = Assert.IsType<AgentJob>(ok.Value);
        Assert.Equal("job-777", value.Id);
    }

    [Fact]
    public async Task GetJob_WhenJobDoesNotExist_ReturnsNotFound()
    {
        _storeMock
            .Setup(store => store.GetAsync<AgentJob>("missing-job", "AgentJobs", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentJob?)null);

        var result = await _sut.GetJob("missing-job");

        Assert.IsType<NotFoundResult>(result);
    }
}
