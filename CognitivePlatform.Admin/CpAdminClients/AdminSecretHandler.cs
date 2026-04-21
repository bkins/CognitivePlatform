using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Extensions.Configuration;

namespace CognitivePlatform.Admin.CpAdminClients;

/// <summary>
/// Injects the X-Admin-Secret header on every outbound request to the CP API.
/// Registered as a transient DelegatingHandler on all admin HttpClient instances.
/// </summary>
public sealed class AdminSecretHandler : DelegatingHandler
{
    private readonly IConfiguration _configuration;

    public AdminSecretHandler(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request
                                                          , CancellationToken   ct)
    {
        var secret = _configuration["AdminSettings:AdminSecret"];

        if (secret.HasValue()) request.Headers.TryAddWithoutValidation("X-Admin-Secret", secret);

        return base.SendAsync(request, ct);
    }
}
