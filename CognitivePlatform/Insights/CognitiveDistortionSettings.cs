namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Persisted flag that gates the CognitiveDistortionInsightProvider.
/// Stored in IObjectStore under key "cognitive_distortion_detector_enabled".
/// Opt-in is off by default.
/// </summary>
public sealed class CognitiveDistortionSettings
{
    public bool IsEnabled { get; set; }
}
