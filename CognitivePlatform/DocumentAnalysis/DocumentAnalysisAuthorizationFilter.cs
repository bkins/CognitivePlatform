using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.DocumentAnalysis;

public sealed class DocumentAnalysisAuthorizationFilter : IAsyncAuthorizationFilter
{
    private readonly IOptionsMonitor<DocumentAnalysisSettings> _settings;
    private readonly IWebHostEnvironment                      _environment;

    public DocumentAnalysisAuthorizationFilter( IOptionsMonitor<DocumentAnalysisSettings> settings
                                              , IWebHostEnvironment environment )
    {
        _settings = settings;
        _environment = environment;
    }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var settings = _settings.CurrentValue;
        var supplied = context.HttpContext.Request.Headers["X-Document-Analysis-Key"];
        byte[] expected;
        try { expected = Convert.FromHexString(settings.ClientKeySha256 ?? string.Empty); }
        catch (FormatException) { expected = []; }

        if (!_environment.IsDevelopment() || !settings.Enabled || expected.Length != 32)
            context.Result = new ObjectResult(new { code = "analysis_disabled" }) { StatusCode = 503 };
        else if (!context.HttpContext.Request.IsHttps
              && (context.HttpContext.Connection.RemoteIpAddress is not { } remoteAddress || !System.Net.IPAddress.IsLoopback(remoteAddress)))
            context.Result = new ObjectResult(new { code = "https_required" }) { StatusCode = 403 };
        else if (supplied.Count != 1 || supplied[0] is not { Length: >= 32 and <= 256 } key
              || !CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(Encoding.UTF8.GetBytes(key))))
            context.Result = new UnauthorizedObjectResult(new { code = "invalid_analysis_credential" });

        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    }
}
