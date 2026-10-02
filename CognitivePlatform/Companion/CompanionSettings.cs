namespace CognitivePlatform.Api.Companion;

public sealed class CompanionSettings
{
    public bool Enabled { get; set; }
    public string ClientKeySha256 { get; set; } = string.Empty;
    public List<CompanionWorkspace> Workspaces { get; set; } = [];
}
