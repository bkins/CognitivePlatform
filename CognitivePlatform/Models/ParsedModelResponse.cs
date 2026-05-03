namespace CognitivePlatform.Api.Models;

public class ParsedModelResponse
{
    public string?                    ActionName        { get; init; }
    public Dictionary<string, string> Parameters        { get; init; } = new();
    public string                     DebugInfo         { get; init; } = "";
    public string?                    Reason            { get; init; }
    public InterpreterFailureType     FailureType       { get; init; } = InterpreterFailureType.None;
    public IReadOnlyList<string>?     CandidateActions  { get; init; }
    public IReadOnlyList<string>?     MissingParameters { get; init; }
    public GeminiErrorResponse?       GeminiError       { get; init; } = null;
}
/*
 * Rate limit exceeded for the day. Please try again tomorrow.
HTTP 429: [{
  "error": {
    "code": 429,
    "message": "You exceeded your current quota, please check your plan and billing details. For more information on this error, head to: https://ai.google.dev/gemini-api/docs/rate-limits. To monitor your current usage, head to: https://ai.dev/rate-limit. \n* Quota exceeded for metric: generativelanguage.googleapis.com/generate_content_free_tier_requests, limit: 20, model: gemini-2.5-flash\nPlease retry in 12.570161799s.",
    "status": "RESOURCE_EXHAUSTED",
    "details": [
      {
        "@type": "type.googleapis.com/google.rpc.Help",
        "links": [
          {
            "description": "Learn more about Gemini API quotas",
            "url": "https://ai.google.dev/gemini-api/docs/rate-limits"
          }
        ]
      },
      {
        "@type": "type.googleapis.com/google.rpc.QuotaFailure",
        "violations": [
          {
            "quotaMetric": "generativelanguage.googleapis.com/generate_content_free_tier_requests",
            "quotaId": "GenerateRequestsPerDayPerProjectPerModel-FreeTier",
            "quotaDimensions": {
              "location": "global",
              "model": "gemini-2.5-flash"
            },
            "quotaValue": "20"
          }
        ]
      },
      {
        "@type": "type.googleapis.com/google.rpc.RetryInfo",
        "retryDelay": "12s"
      }
    ]
  }
}
]
 */