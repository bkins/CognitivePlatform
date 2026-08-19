namespace CognitivePlatform.Admin.CpAdminClients;

/// <summary>Response from GET /api/admin/system/stats.</summary>
public sealed record SystemStatsResponse
{
    public string                             EnvironmentName  { get; init; } = string.Empty;
    public string                             DatabasePath     { get; init; } = string.Empty;
    public string                             LlmProvider      { get; init; } = string.Empty;
    public string                             LlmModel         { get; init; } = string.Empty;
    public GroqUsageDto?                      GroqUsage        { get; init; }
    public IReadOnlyList<ObjectCountDto>      ObjectCounts     { get; init; } = [];
    public IReadOnlyList<LanIntegrationDto>   LanIntegrations  { get; init; } = [];
}

public sealed record LanIntegrationDto
{
    public string Name          { get; init; } = string.Empty;
    public string ConfigKey     { get; init; } = string.Empty;
    public string ConfiguredUrl { get; init; } = string.Empty;
    public bool   IsConfigured  { get; init; }
    public string Note          { get; init; } = string.Empty;
}

public sealed record GroqUsageDto
{
    public bool            HasData    { get; init; }
    public DateTimeOffset? CapturedAt { get; init; }
    public GroqRateDto     Requests   { get; init; } = new();
    public GroqRateDto     Tokens     { get; init; } = new();
}

public sealed record GroqRateDto
{
    public int    Limit        { get; init; }
    public int    Remaining    { get; init; }
    public int    Used         { get; init; }
    public double UsagePercent { get; init; }
}

public sealed record ObjectCountDto
{
    public string TypeName    { get; init; } = string.Empty;
    public int    Total       { get; init; }
    public int    SoftDeleted { get; init; }
}

public sealed record TelemetryMetricsDto
{
    public string   OperationName     { get; init; } = string.Empty;
    public int      Count             { get; init; }
    public double   AverageDurationMs { get; init; }
    public double   MinDurationMs     { get; init; }
    public double   MaxDurationMs     { get; init; }
    public double   SuccessRate       { get; init; }
    public DateTime LastActivity      { get; init; }
}
