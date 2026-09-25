namespace CognitivePlatform.Admin.Services;

public sealed record ReleaseSourceIdentity( string  CognitivePlatformCommitHash
                                          , string? LocalAIAssistantCommitHash)
{
    public string Value => LocalAIAssistantCommitHash is null
        ? $"CognitivePlatform={CognitivePlatformCommitHash}"
        : $"CognitivePlatform={CognitivePlatformCommitHash}|LocalAIAssistant={LocalAIAssistantCommitHash}";
}
