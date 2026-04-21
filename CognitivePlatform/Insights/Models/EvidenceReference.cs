namespace CognitivePlatform.Api.Insights.Models;

/// <summary>
/// A typed reference to the data that triggered an insight.
/// Example: ("JournalEntry", "abc123"), ("Task", "def456")
/// </summary>
public sealed record EvidenceReference(string EntityType, string EntityId);
