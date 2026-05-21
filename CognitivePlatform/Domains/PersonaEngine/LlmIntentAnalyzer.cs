using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.PersonaEngine.Models;
using CognitivePlatform.Api.Interpreter;

namespace CognitivePlatform.Api.Domains.PersonaEngine;

public class LlmIntentAnalyzer : IIntentAnalyzer
{
    private readonly ILlmRouter _llmRouter;

    public LlmIntentAnalyzer(ILlmRouter llmRouter)
    {
        _llmRouter = llmRouter ?? throw new ArgumentNullException(nameof(llmRouter));
    }

    public async Task<IntentAnalysisResult> AnalyzeAsync(string message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return new IntentAnalysisResult
                   {
                       Intent     = Intent.Unknown
                     , Confidence = 0.0
                   };
        }

        try
        {
            var intentNames = string.Join(", ", Enum.GetNames(typeof(Intent)));
            var prompt      = $"Classify this input into exactly one of [{intentNames}]: {message}\nRespond with only the label, nothing else.";

            var ephemeralContext = new ConversationContext("intent-analysis", new ConversationContextOptions());
            var response         = await _llmRouter.SendAsync(prompt, ephemeralContext, ct: ct).ConfigureAwait(false);
            var firstLine        = response.Content
                                           .Trim()
                                           .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                           .FirstOrDefault()
                                           ?.Trim()
                                   ?? string.Empty;

            if (!Enum.TryParse<Intent>(firstLine, ignoreCase: true, out var intent))
                intent = Intent.Unknown;

            return new IntentAnalysisResult
                   {
                       Intent               = intent
                     , Confidence           = intent != Intent.Unknown ? 0.9 : 0.0
                     , SuggestedPersonaName = intent != Intent.Unknown ? intent.ToString() : null
                   };
        }
        catch
        {
            return new IntentAnalysisResult
                   {
                       Intent     = Intent.Unknown
                     , Confidence = 0.0
                   };
        }
    }
}
