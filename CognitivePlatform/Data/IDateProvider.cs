namespace CognitivePlatform.Api.Data;

/// <summary>
/// Provides date and time abstraction to decouple domain services from static Environment/DateTime calls.
/// </summary>
public interface IDateProvider
{
    DateOnly Today  { get; }
    DateTime UtcNow { get; }
    DateTime Now    { get; }
}
