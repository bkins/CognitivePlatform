namespace CognitivePlatform.Api.Models;

public sealed record WhyActionResult
(
    string?                ActionName
  , string?                Reason
  , string                 Debug
  , InterpreterFailureType FailureType
  , IReadOnlyList<string>? CandidateActions
  , IReadOnlyList<string>? MissingParameters
);