namespace CognitivePlatform.Api.Domains.Tasks;

public enum BatchCompleteOutcome
{
    Completed
  , AlreadyCompleted
  , NotFound
}

public sealed record BatchCompleteResult( string               TaskId
                                        , string               ShortDescription
                                        , BatchCompleteOutcome Outcome
                                        , DateTimeOffset?      CompletedAt
);