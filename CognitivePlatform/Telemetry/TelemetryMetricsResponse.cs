namespace CognitivePlatform.Api.Telemetry;

public sealed class TelemetryMetricsResponse
{
    public string   OperationName     { get; set; } = string.Empty;
    public int      Count             { get; set; }
    public double   AverageDurationMs { get; set; }
    public double   MinDurationMs     { get; set; }
    public double   MaxDurationMs     { get; set; }
    public double   SuccessRate       { get; set; }
    public DateTime LastActivity      { get; set; }
}
