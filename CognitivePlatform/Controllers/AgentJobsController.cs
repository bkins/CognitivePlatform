using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Agent;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/agent")]
public class AgentJobsController : ControllerBase
{
    private readonly IObjectStore _objectStore;

    public AgentJobsController(IObjectStore objectStore)
    {
        _objectStore = objectStore;
    }

    [HttpPost("jobs")]
    public async Task<IActionResult> CreateJob([FromBody] CreateAgentJobRequest request)
    {
        var job = new AgentJob
                  {
                      Prompt         = request.Prompt
                    , ConversationId = request.ConversationId
                    , Model          = request.Model
                    , Status         = AgentJobStatus.Pending
                    , CreatedUtc     = DateTimeOffset.UtcNow
                  };

        await _objectStore.Save(job, "AgentJobs", job.Id);
        return Ok(job);
    }

    [HttpGet("jobs/pending")]
    public async Task<IActionResult> GetPendingJobs()
    {
        var jobs = await _objectStore.ListAsync<AgentJob>("AgentJobs");
        var pending = jobs.Where(job => job.Status == AgentJobStatus.Pending && !job.IsDeleted).ToList();
        return Ok(pending);
    }

    [HttpGet("jobs/running")]
    public async Task<IActionResult> GetRunningJobs()
    {
        var jobs = await _objectStore.ListAsync<AgentJob>("AgentJobs");
        var running = jobs.Where(job => job.Status == AgentJobStatus.Running && !job.IsDeleted).ToList();
        return Ok(running);
    }

    [HttpPost("jobs/{id}/start")]
    public async Task<IActionResult> StartJob([FromRoute] string id, [FromQuery] string? conversationId = null)
    {
        var job = await _objectStore.GetAsync<AgentJob>(id, "AgentJobs");
        if (job is null || job.IsDeleted)
            return NotFound();

        job.Status = AgentJobStatus.Running;
        job.StartedUtc = DateTimeOffset.UtcNow;
        if (conversationId.HasValue())
        {
            job.ConversationId = conversationId;
        }

        await _objectStore.Save(job, "AgentJobs", id);
        return Ok(job);
    }

    [HttpPost("jobs/{id}/complete")]
    public async Task<IActionResult> CompleteJob([FromRoute] string id, [FromBody] CompleteAgentJobRequest request)
    {
        var job = await _objectStore.GetAsync<AgentJob>(id, "AgentJobs");
        if (job is null || job.IsDeleted)
            return NotFound();

        job.Status = AgentJobStatus.Completed;
        job.Response = request.Response;
        job.CompletedUtc = DateTimeOffset.UtcNow;
        if (request.ConversationId.HasValue())
        {
            job.ConversationId = request.ConversationId;
        }

        await _objectStore.Save(job, "AgentJobs", id);
        return Ok(job);
    }

    [HttpPost("jobs/{id}/fail")]
    public async Task<IActionResult> FailJob([FromRoute] string id, [FromBody] FailAgentJobRequest request)
    {
        var job = await _objectStore.GetAsync<AgentJob>(id, "AgentJobs");
        if (job is null || job.IsDeleted)
            return NotFound();

        job.Status = AgentJobStatus.Failed;
        job.Error = request.Error;
        job.CompletedUtc = DateTimeOffset.UtcNow;

        await _objectStore.Save(job, "AgentJobs", id);
        return Ok(job);
    }

    [HttpGet("jobs/{id}")]
    public async Task<IActionResult> GetJob([FromRoute] string id)
    {
        var job = await _objectStore.GetAsync<AgentJob>(id, "AgentJobs");
        if (job is null || job.IsDeleted)
            return NotFound();

        return Ok(job);
    }
}
