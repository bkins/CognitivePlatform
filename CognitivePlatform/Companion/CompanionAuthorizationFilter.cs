using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Companion;

public sealed class CompanionAuthorizationFilter(IOptionsMonitor<CompanionSettings> settings, IWebHostEnvironment environment) : IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var current = settings.CurrentValue;
        var supplied = context.HttpContext.Request.Headers["X-Companion-Key"];
        byte[] expected;
        try { expected = Convert.FromHexString(current.ClientKeySha256 ?? string.Empty); }
        catch (FormatException) { expected = []; }
        if (!environment.IsDevelopment() || !current.Enabled || expected.Length != 32)
            context.Result = new ObjectResult(new { code = "companion_disabled" }) { StatusCode = 503 };
        else if (!context.HttpContext.Request.IsHttps
              && (context.HttpContext.Connection.RemoteIpAddress is not { } remoteAddress || !System.Net.IPAddress.IsLoopback(remoteAddress)))
            context.Result = new ObjectResult(new { code = "https_required" }) { StatusCode = 403 };
        else if (supplied.Count != 1 || supplied[0] is not { Length: >= 32 and <= 256 } key
              || !CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(Encoding.UTF8.GetBytes(key))))
            context.Result = new UnauthorizedObjectResult(new { code = "invalid_companion_credential" });
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    }
}
