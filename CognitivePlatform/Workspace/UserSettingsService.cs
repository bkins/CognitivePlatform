using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Api.Workspace;

/// <summary>
/// Reads and writes <see cref="UserSettings"/> as a singleton blob in
/// <see cref="IObjectStore"/>. A fixed well-known ID ensures there is always
/// exactly one record per installation.
/// </summary>
public sealed class UserSettingsService : IUserSettingsService
{
    /// <summary>
    /// The fixed object-store ID for the singleton UserSettings record.
    /// Exposed so <see cref="UserSettings"/> can reference it in its default value.
    /// </summary>
    public const string SettingsId = "user-settings";

    private readonly IObjectStore _store;

    public UserSettingsService(IObjectStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public UserSettings Get() =>
        _store.Get<UserSettings>(SettingsId, partitionKey: null)
            ?? new UserSettings();

    /// <inheritdoc />
    public Task SaveAsync(UserSettings settings) =>
        _store.Save(settings, partitionKey: null, id: SettingsId);
}
