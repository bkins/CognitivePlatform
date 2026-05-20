using CognitivePlatform.Api.Domains.PersonaEngine.Models;

namespace CognitivePlatform.Api.Domains.PersonaEngine;

public interface IIntentAnalyzer
{
    Task<IntentAnalysisResult> AnalyzeAsync(string message, CancellationToken ct = default);
}
