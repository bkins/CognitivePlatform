namespace CognitivePlatform.Api.Workspace;

/// <summary>
/// Reads and writes the singleton <see cref="UserSettings"/> record.
/// </summary>
public interface IUserSettingsService
{
    /// <summary>
    /// Returns the current user settings. If no record exists (e.g. a fresh
    /// installation), returns a default-valued <see cref="UserSettings"/> instance.
    /// Never returns null.
    /// </summary>
    UserSettings Get();

    /// <summary>Persists the supplied settings, replacing any existing record.</summary>
    Task SaveAsync(UserSettings settings);
}
