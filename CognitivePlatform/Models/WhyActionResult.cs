namespace CognitivePlatform.Api.Models;

public sealed record WhyActionResult
(
    string? ActionName
  , string? Reason
  , string  Debug
);