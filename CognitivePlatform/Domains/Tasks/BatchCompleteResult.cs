namespace CognitivePlatform.Api.Domains.Tasks;

public sealed record BatchCompleteResult( string               TaskId
                                        , string               ShortDescription
                                        , BatchCompleteOutcome Outcome
                                        , DateTimeOffset?      CompletedAt
);