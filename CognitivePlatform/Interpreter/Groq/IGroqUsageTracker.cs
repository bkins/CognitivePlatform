namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Singleton store for the most recent Groq rate-limit snapshot.
/// Written by <see cref="GroqLlmClient"/> after every successful response;
/// read by <see cref="CognitivePlatform.Api.Controllers.SystemController"/>.
/// </summary>
public interface IGroqUsageTracker
{
    /// <summary>
    /// The most recent snapshot. Never null — returns <see cref="GroqUsageSnapshot.Empty"/> until first call.
    /// </summary>
    GroqUsageSnapshot Current { get; }

    void Update(GroqUsageSnapshot snapshot);
}