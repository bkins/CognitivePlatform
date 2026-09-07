namespace CognitivePlatform.Api.Avails;

/// <summary>
/// Holds the model selected by startup probing for this API process only.
/// Configuration remains the next-startup preference and is never changed here.
/// </summary>
public sealed class RuntimeLlmModelState
{
    public string ConfiguredModel { get; private set; } = string.Empty;
    public string EffectiveModel  { get; private set; } = string.Empty;

    public void SetConfiguredModel(string model)
    {
        ConfiguredModel = model;
        EffectiveModel  = model;
    }

    public void SetEffectiveModel(string model)
    {
        EffectiveModel = model;
    }
}
