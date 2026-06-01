using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Domains.Cognition;

[Domain(typeof(CognitionDomain))]
public class CognitionActions
{
    private readonly IObjectStore _objectStore;

    public CognitionActions(IObjectStore objectStore)
    {
        _objectStore = objectStore ?? throw new ArgumentNullException(nameof(objectStore));
    }

    // -----------------------------------------------------------------------
    // EnableCognitiveDistortionDetector
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Enables the cognitive distortion detector, which gently reflects all-or-nothing, catastrophising, or mind-reading language noticed in recent journal entries."
                         , Examples   = new[]
                                        {
                                                "enable distortion detection"
                                              , "turn on cognitive distortion detection"
                                              , "start noticing my thinking patterns"
                                              , "enable cognitive distortion detector"
                                        }
                         , Category   = "cognition")]
    public async Task<string> EnableCognitiveDistortionDetector()
    {
        await _objectStore.Save(new CognitiveDistortionSettings { IsEnabled = true }
                              , id: CognitiveDistortionInsightProvider.FlagKey);

        return "Cognitive distortion detection is now on. I'll gently reflect any all-or-nothing or catastrophising language I notice in your journals — no judgment, just curiosity.";
    }

    // -----------------------------------------------------------------------
    // DisableCognitiveDistortionDetector
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Disables the cognitive distortion detector so journal language patterns are no longer surfaced."
                         , Examples   = new[]
                                        {
                                                "disable distortion detection"
                                              , "turn off cognitive distortion detection"
                                              , "stop noticing my thinking patterns"
                                              , "disable cognitive distortion detector"
                                        }
                         , Category   = "cognition")]
    public async Task<string> DisableCognitiveDistortionDetector()
    {
        await _objectStore.Save(new CognitiveDistortionSettings { IsEnabled = false }
                              , id: CognitiveDistortionInsightProvider.FlagKey);

        return "Cognitive distortion detection is now off.";
    }
}
