using System.Text.Json.Serialization;

namespace CognitivePlatform.Api.Insights.Models;

/// <summary>
/// Persisted record of every check and outcome performed by the Automation Gate.
/// </summary>
public sealed class AutomationAudit
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;
    public string ActionName { get; set; } = string.Empty;
    public string ParametersJson { get; set; } = string.Empty;
    public bool Allowed { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedUtc { get; set; }
}
