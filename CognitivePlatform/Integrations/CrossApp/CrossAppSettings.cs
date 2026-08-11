namespace CognitivePlatform.Api.Integrations.CrossApp;

public sealed class CrossAppSettings
{
    public WatchListSettings WatchList { get; set; } = new();
}

public sealed class WatchListSettings
{
    public string DbPath { get; set; } = string.Empty;
    public string ApiUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; } = false;
}
