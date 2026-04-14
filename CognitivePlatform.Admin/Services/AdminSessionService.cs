namespace CognitivePlatform.Admin.Services;

/// <summary>
/// Scoped per SignalR circuit — tracks auth state for the lifetime of the browser session.
/// On page refresh the user must re-authenticate. Intentional: this is a single-dev local tool.
/// </summary>
public sealed class AdminSessionService
{
    public bool IsAuthenticated { get; private set; }

    public bool TryAuthenticate(string passphrase, string expectedSecret)
    {
        if (string.IsNullOrWhiteSpace(passphrase)
         || string.IsNullOrWhiteSpace(expectedSecret))
            return false;

        IsAuthenticated = string.Equals(passphrase
                                      , expectedSecret
                                      , StringComparison.Ordinal);

        return IsAuthenticated;
    }

    public void SignOut() => IsAuthenticated = false;
}
