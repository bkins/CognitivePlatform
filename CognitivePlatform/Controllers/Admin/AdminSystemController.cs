using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Interpreter;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers.Admin;

[Route("api/admin/system")]
public sealed class AdminSystemController : AdminControllerBase
{
    private readonly SqliteObjectStore   _store;
    private readonly IWebHostEnvironment _hostEnvironment;
    private readonly IGroqUsageTracker   _usageTracker;

    public AdminSystemController( IConfiguration      configuration
                                , SqliteObjectStore   store
                                , IWebHostEnvironment hostEnvironment
                                , IGroqUsageTracker   usageTracker )
            : base(configuration)
    {
        _store           = store;
        _hostEnvironment = hostEnvironment;
        _usageTracker    = usageTracker;
    }

    /// <summary>
    /// Returns environment info, LLM config, Groq rate-limit snapshot,
    /// and per-type object store counts (total + soft-deleted).
    /// </summary>
    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var counts  = _store.GetObjectTypeCounts();
        var usage   = _usageTracker.Current;
        var envName = _hostEnvironment.EnvironmentName;
        var dbPath  = Path.Combine(_hostEnvironment.ContentRootPath
                                 , "Data"
                                 , envName
                                 , "platform.db");

        return Ok(new
                  {
                          EnvironmentName = envName
                        , DatabasePath    = dbPath
                        , LlmProvider     = _configuration["LlmClient:Provider"]     ?? string.Empty
                        , LlmModel        = _configuration["LlmClient:DefaultModel"] ?? string.Empty
                        , GroqUsage       = new
                                           {
                                                   usage.HasData
                                                 , usage.CapturedAt
                                                 , Requests = new
                                                              {
                                                                      Limit        = usage.RequestLimit
                                                                    , Remaining    = usage.RequestsRemaining
                                                                    , Used         = usage.RequestsUsed
                                                                    , UsagePercent = usage.RequestUsagePercent
                                                              }
                                                 , Tokens   = new
                                                              {
                                                                      Limit        = usage.TokenLimit
                                                                    , Remaining    = usage.TokensRemaining
                                                                    , Used         = usage.TokensUsed
                                                                    , UsagePercent = usage.TokenUsagePercent
                                                              }
                                           }
                        , ObjectCounts    = counts
                  });
    }
}
