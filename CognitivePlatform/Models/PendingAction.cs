namespace CognitivePlatform.Api.Models;

public sealed class PendingAction
{
    public string ActionName { get; init; } = string.Empty;

    /// <summary>
    /// Parameters we already have values for (from the interpreter).
    /// </summary>
    public Dictionary<string, string> CollectedParameters { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Names of parameters we still need from the user.
    /// For Step 3.8 we mainly care about the first one, but this keeps us future-proof.
    /// </summary>
    public List<string> RemainingParameters { get; init; } = new();
}