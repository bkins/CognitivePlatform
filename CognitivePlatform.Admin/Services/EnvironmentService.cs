namespace CognitivePlatform.Admin.Services;

/// <summary>
/// Singleton — all circuits share the same active environment so every tab/request
/// targets the same API. Switching in one tab immediately affects all in-flight calls.
/// </summary>
public sealed class EnvironmentService
{
    public string DevUrl  { get; }
    public string QaUrl   { get; }
    public string ProdUrl { get; }

    public string Current { get; private set; } = "DEV";

    public EnvironmentService(IConfiguration config)
    {
        DevUrl  = config["AdminSettings:Environments:Dev"]  ?? "http://localhost:5273";
        QaUrl   = config["AdminSettings:Environments:QA"]   ?? "http://localhost:5274";
        ProdUrl = config["AdminSettings:Environments:Prod"] ?? "http://localhost:5275";
    }

    public void Switch(string env) => Current = env.ToUpperInvariant();

    public string BaseUrl => Current switch
    {
        "QA"   => QaUrl
      , "PROD" => ProdUrl
      , _      => DevUrl
    };
}
