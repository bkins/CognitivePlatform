using CognitivePlatform.Api.Audit;
using CognitivePlatform.Api.Contracts;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers.Admin;

[Route("api/admin/trust-traces")]
public sealed class AdminTrustTraceController : AdminControllerBase
{
    private readonly ITrustTraceStore _traces;

    public AdminTrustTraceController( IConfiguration     configuration
                                    , ITrustTraceStore    traces)
        : base(configuration)
    {
        _traces = traces;
    }

    [HttpGet]
    public IActionResult Get([FromQuery] int take = 100)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var traces = _traces.List(take)
                            .Select(trace => new TrustTraceDto(trace.Id
                                                               , trace.OccurredUtc
                                                               , trace.ActionName
                                                               , trace.Success
                                                               , trace.Items))
                            .ToList();
        return Ok(traces);
    }
}

public sealed record TrustTraceDto( string                          Id
                                  , DateTimeOffset                  OccurredUtc
                                  , string?                         ActionName
                                  , bool                            Success
                                  , IReadOnlyList<TransparencyItem> Items );
