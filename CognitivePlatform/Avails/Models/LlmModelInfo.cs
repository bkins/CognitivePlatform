namespace CognitivePlatform.Api.Avails.Models;

public record LlmModelInfo
(
    string  Name
  , bool    IsUsable
  , string? FailureReason
  , bool    SupportsChat
  , bool    SupportsStreaming
);
