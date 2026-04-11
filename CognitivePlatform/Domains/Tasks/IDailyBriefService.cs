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
    /// <summary>Returns the formatted daily brief for the current moment in time.</summary>
    string GetBrief();
}
