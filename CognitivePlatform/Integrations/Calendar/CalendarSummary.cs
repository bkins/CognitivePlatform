namespace CognitivePlatform.Api.Integrations.Calendar;

/// <summary>
/// A calendar visible to the authenticated user, with its current inclusion state.
/// Returned by <see cref="ICalendarProvider.GetCalendarListAsync"/>.
/// </summary>
public sealed record CalendarSummary(string Id, string Name, bool IsSelected);
