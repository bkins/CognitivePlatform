using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

/// <summary>
/// Base class for all admin API controllers.
///
/// Every derived action must call IsAdminAuthorized() at the top and return
/// Unauthorized401() if it returns false. Pattern:
///
///     if (IsAdminAuthorized().Not()) return Unauthorized401();
///
/// The gate checks the X-Admin-Secret request header against
/// AdminSettings:AdminSecret in configuration. An empty or missing secret
/// is always rejected — misconfiguration fails closed.
/// </summary>
[ApiController]
[Route("api/admin")]
public abstract class AdminControllerBase : ControllerBase
{
    protected readonly IConfiguration _configuration;

    protected AdminControllerBase(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected bool IsAdminAuthorized()
    {
        var expectedSecret = _configuration["AdminSettings:AdminSecret"];

        if (expectedSecret.HasNoValue())
            return false;

        Request.Headers.TryGetValue("X-Admin-Secret", out var providedSecret);

        return string.Equals(expectedSecret
                           , providedSecret
                           , StringComparison.Ordinal);
    }

    protected IActionResult Unauthorized401()
        => StatusCode(StatusCodes.Status401Unauthorized
                    , "Admin access denied.");
}
