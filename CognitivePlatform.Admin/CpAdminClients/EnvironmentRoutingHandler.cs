using CognitivePlatform.Admin.Services;

namespace CognitivePlatform.Admin.CpAdminClients;

/// <summary>
/// Rewrites the placeholder base URL on every outbound request to the URL of the
/// currently selected environment (DEV / QA / PROD).
/// All typed clients use "http://cp-placeholder/" as their BaseAddress; this handler
/// substitutes the real scheme + host + port before the request is sent.
/// </summary>
public sealed class EnvironmentRoutingHandler : DelegatingHandler
{
    private readonly EnvironmentService _env;

    public EnvironmentRoutingHandler(EnvironmentService env)
    {
        _env = env;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri is { Host: "cp-placeholder" })
        {
            var target = new Uri(_env.BaseUrl);
            request.RequestUri = new UriBuilder(request.RequestUri)
                                  {
                                      Scheme = target.Scheme
                                    , Host   = target.Host
                                    , Port   = target.Port
                                  }.Uri;
        }

        return base.SendAsync(request, ct);
    }
}
