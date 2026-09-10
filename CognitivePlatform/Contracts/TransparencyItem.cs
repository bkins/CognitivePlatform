namespace CognitivePlatform.Api.Contracts;

/// <summary>
/// A display-safe, deterministic fact explaining a completed conversation turn.
/// It deliberately excludes private inputs, raw parameters, and model chain-of-thought.
/// </summary>
public sealed record TransparencyItem(string Label, string Detail);
