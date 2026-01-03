namespace CognitivePlatform.Api.Models;

public sealed record LlmModelProbeResult
(
  string  Model
, bool    IsUsable
, string? Error         = null
, string? FailureReason = null
);
