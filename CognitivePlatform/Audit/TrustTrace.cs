using CognitivePlatform.Api.Contracts;

namespace CognitivePlatform.Api.Audit;

/// <summary>
/// A durable, display-safe explanation of a finalized turn for operator review.
/// It intentionally contains no user input, parameters, prompts, session identity, or diagnostics.
/// </summary>
public sealed class TrustTrace
{
    public string                          Id          { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset                  OccurredUtc { get; init; } = DateTimeOffset.UtcNow;
    public string?                         ActionName  { get; init; }
    public bool                            Success     { get; init; }
    public IReadOnlyList<TransparencyItem> Items       { get; init; } = Array.Empty<TransparencyItem>();
}
