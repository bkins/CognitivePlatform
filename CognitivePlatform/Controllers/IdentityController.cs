using CognitivePlatform.Api.Domains.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/identity")]
public sealed class IdentityController : ControllerBase
{
    private readonly IIdentityService         _service;
    private readonly IIdentityAnalysisService _analysisService;

    public IdentityController( IIdentityService         service
                             , IIdentityAnalysisService analysisService )
    {
        _service         = service;
        _analysisService = analysisService;
    }

    [HttpGet("profile")]
    public async Task<ActionResult<PersonProfile>> GetProfile(CancellationToken ct)
    {
        var profile = await _service.GetProfileAsync(ct);
        return Ok(profile);
    }

    [HttpPut("profile")]
    public async Task<ActionResult<PersonProfile>> UpdateProfile( [FromBody]       PersonProfile  profile
                                                                 , CancellationToken               ct )
    {
        await _service.UpdateProfileAsync(profile, ct);
        return Ok(profile);
    }

    [HttpGet("assertions")]
    public async Task<ActionResult<IReadOnlyList<IdentityAssertion>>> GetAssertions(CancellationToken ct)
    {
        var assertions = await _service.GetAssertionsAsync(ct);
        return Ok(assertions);
    }

    [HttpPost("assertions")]
    public async Task<ActionResult<IdentityAssertion>> AddAssertion( [FromBody]       IdentityAssertion  assertion
                                                                    , CancellationToken                   ct )
    {
        await _service.AddAssertionAsync(assertion, ct);
        return Ok(assertion);
    }

    [HttpPut("assertions/{id}/confirm")]
    public async Task<IActionResult> ConfirmAssertion( [FromRoute] string            id
                                                      , CancellationToken             ct )
    {
        await _service.ConfirmAssertionAsync(id, ct);
        return Ok($"Assertion '{id}' confirmed.");
    }

    // ================================================================
    // Derived Insights
    // ================================================================

    [HttpGet("insights")]
    public async Task<ActionResult<IReadOnlyList<DerivedInsight>>> GetInsights(CancellationToken ct)
    {
        var insights = await _service.GetDerivedInsightsAsync(ct);
        return Ok(insights);
    }

    [HttpPost("insights/generate")]
    public async Task<ActionResult<IReadOnlyList<DerivedInsight>>> GenerateInsights(CancellationToken ct)
    {
        var insights = await _analysisService.GenerateInsightsAsync(string.Empty, ct);
        return Ok(insights);
    }

    [HttpPut("insights/{id}/confirm")]
    public async Task<IActionResult> ConfirmInsight( [FromRoute] string            id
                                                    , CancellationToken             ct )
    {
        await _service.ConfirmDerivedInsightAsync(id, ct);
        return Ok($"Insight '{id}' confirmed.");
    }

    // ================================================================
    // Personality Snapshots
    // ================================================================

    [HttpGet("snapshots")]
    public async Task<ActionResult<IReadOnlyList<PersonalitySnapshot>>> GetSnapshots(CancellationToken ct)
    {
        var snapshots = await _service.GetSnapshotsAsync(ct);
        return Ok(snapshots);
    }

    [HttpPost("snapshots/generate")]
    public async Task<ActionResult<PersonalitySnapshot>> GenerateSnapshot(CancellationToken ct)
    {
        var snapshot = await _analysisService.GenerateSnapshotAsync(string.Empty, ct);
        return Ok(snapshot);
    }

    [HttpGet("snapshots/latest")]
    public async Task<ActionResult<PersonalitySnapshot?>> GetLatestSnapshot(CancellationToken ct)
    {
        var snapshot = await _service.GetLatestSnapshotAsync(ct);
        return Ok(snapshot);
    }

    // ================================================================
    // Phase C — reflective intelligence
    // ================================================================

    [HttpGet("reflect")]
    public async Task<ActionResult<string>> ReflectOnChange( [FromQuery] string?           window
                                                            , CancellationToken             ct )
    {
        var timespan = ParseWindowParameter(window);
        var narrative = await _analysisService.ReflectOnChangeAsync(string.Empty, timespan, ct);
        return Ok(narrative);
    }

    [HttpGet("patterns")]
    public async Task<ActionResult<string>> IdentifyPatterns(CancellationToken ct)
    {
        var narrative = await _analysisService.IdentifyPatternsAsync(string.Empty, ct);
        return Ok(narrative);
    }

    [HttpGet("stressors/analysis")]
    public async Task<ActionResult<string>> AnalyzeStressors(CancellationToken ct)
    {
        var narrative = await _analysisService.AnalyzeStressorsAsync(string.Empty, ct);
        return Ok(narrative);
    }

    [HttpGet("growth")]
    public async Task<ActionResult<string>> SummarizeGrowth(CancellationToken ct)
    {
        var narrative = await _analysisService.SummarizeGrowthAsync(string.Empty, ct);
        return Ok(narrative);
    }

    [HttpGet("snapshots/compare")]
    public async Task<ActionResult<string>> CompareSnapshots( [FromQuery] DateTime          from
                                                             , [FromQuery] DateTime          to
                                                             , CancellationToken             ct )
    {
        var narrative = await _analysisService.CompareSnapshotsAsync(string.Empty, from, to, ct);
        return Ok(narrative);
    }

    // ----------------------------------------------------------------

    private static TimeSpan ParseWindowParameter(string? window)
    {
        if (string.IsNullOrWhiteSpace(window))
            return TimeSpan.FromDays(90);

        var lower = window.ToLowerInvariant();

        if (lower.EndsWith("d") && int.TryParse(lower.TrimEnd('d'), out var days))
            return TimeSpan.FromDays(days);

        if (lower.EndsWith("m") && int.TryParse(lower.TrimEnd('m'), out var months))
            return TimeSpan.FromDays(months * 30);

        if (lower.EndsWith("y") && int.TryParse(lower.TrimEnd('y'), out var years))
            return TimeSpan.FromDays(years * 365);

        return TimeSpan.FromDays(90);
    }
}
