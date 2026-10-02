namespace CognitivePlatform.Api.DocumentAnalysis;

public sealed class DocumentAnalysisSettings
{
    public bool   Enabled           { get; set; }
    public string ClientKeySha256   { get; set; } = string.Empty;
    public int    TimeoutSeconds    { get; set; } = 60;
}
