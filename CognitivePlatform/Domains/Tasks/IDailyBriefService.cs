namespace CognitivePlatform.Api.Domains.Tasks;

/// <summary>
/// Produces a pre-formatted daily brief summarising the most actionable tasks:
/// - Tasks that are both Important and Urgent (Eisenhower "Do It Now")
/// - Tasks whose due date is today or overdue
///
/// The brief is returned as a pre-formatted plain-text string — no LLM involvement.
/// Calendar data will be added as a third section once calendar integration exists
/// (see DEFERRED.md).
/// </summary>
public interface IDailyBriefService
{
    /// <summary>
    /// Returns the formatted daily brief.
    ///
    /// <paramref name="localDate"/> is the user's local calendar date. When supplied,
    /// the calendar window and the "due today / overdue" filter are keyed to that date.
    /// When null, the brief falls back to <c>DateTimeOffset.UtcNow.Date</c> — preserving
    /// the pre-BUG-12 behaviour for callers that do not yet pass a date.
    /// </summary>
    string GetBrief(DateOnly? localDate = null);
}
