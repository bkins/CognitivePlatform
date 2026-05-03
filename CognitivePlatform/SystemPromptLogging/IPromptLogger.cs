namespace CognitivePlatform.Api.SystemPromptLogging;

public interface IPromptLogger
{
    void Log(string description, string prompt, string? provider = null, string? model = null);
}
