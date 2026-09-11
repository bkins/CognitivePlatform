namespace CognitivePlatform.Api.Automation;

public sealed record EngineeringExecutionUsage(
    TimeSpan Elapsed,
    int      ModelRequestCount,
    int      RepairAttemptCount,
    decimal  SpendUsd);
