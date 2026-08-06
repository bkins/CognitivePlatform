using System;
using System.Text;
using System.Threading.Tasks;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Registry.Domains;
using CognitivePlatform.Api.SystemPromptLogging;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Insights;

/// <summary>
/// Cross-domain AI analysis: spans tasks, journal entries, explicit activity events, and meal records to
/// surface patterns, trends, and holistic wellbeing/productivity insights. Also hosts on-demand
/// execution of the reflective intelligence engine.
/// </summary>
[Domain(typeof(KnowledgeDomain))]
public class InsightsActions
{
    private readonly IPatternDataAggregator _aggregator;
    private readonly ILlmClient             _llmClient;
    private readonly IPromptLogger          _promptLogger;
    private readonly IInsightEngine?        _insightEngine;

    public InsightsActions( IPatternDataAggregator aggregator
                          , ILlmClient             llmClient
                          , IPromptLogger          promptLogger
                          , IInsightEngine?        insightEngine = null )
    {
        _aggregator    = aggregator   ?? throw new ArgumentNullException(nameof(aggregator));
        _llmClient     = llmClient    ?? throw new ArgumentNullException(nameof(llmClient));
        _promptLogger  = promptLogger ?? throw new ArgumentNullException(nameof(promptLogger));
        _insightEngine = insightEngine;
    }

    [NaturalLanguageAction(Description = "Analyzes patterns across your tasks and journal entries using AI. Surfaces connections, trends, and holistic productivity or wellbeing insights."
                         , Examples =
                           [
                               "What patterns do you see in my work habits?"
                             , "Am I making progress on the things that matter most?"
                             , "How does my mood relate to my productivity?"
                             , "What recurring themes show up in my tasks and journal?"
                             , "Give me an overall picture of how my week went."
                           ]
                         , Category = "insights")]
    public async Task<string> AnalyzePatterns( [NaturalLanguageParam(Description  = "What to focus the analysis on (e.g. 'productivity', 'mood', 'work-life balance', or leave blank for a general overview)."
                                                                   , Optional     = true
                                                                   , DefaultValue = "general patterns and trends")]
                                               string? focus = null
                                             , [NaturalLanguageParam(Description  = "Start date for the analysis window (optional)."
                                                                   , Optional     = true
                                                                   , DefaultValue = "")]
                                               string? fromDate = null
                                             , [NaturalLanguageParam(Description  = "End date for the analysis window (optional)."
                                                                   , Optional     = true
                                                                   , DefaultValue = "")]
                                               string? toDate = null)
    {
        var prompt = await _aggregator.AggregateAndFormatAsync(focus, fromDate, toDate).ConfigureAwait(false);

        if (prompt is null || prompt.HasNoValue())
            return "No tasks, journal, or activity entries found for the specified date range.";

        _promptLogger.Log("InsightsAnalysisPrompt", prompt!, _llmClient.GetType().Name);

        var response = await _llmClient.SendAsync(prompt!).ConfigureAwait(false);
        return response.Content;
    }

    [FastPath]
    [NaturalLanguageAction(
        Description = "Runs all registered insight providers immediately and returns generated insights."
      , Examples    =
        [
            "Run insights now."
          , "Check my health insights."
          , "Generate wellbeing insights."
        ]
      , Category    = "Wellbeing")]
    public async Task<ActionResult> RunInsightsNow()
    {
        if (_insightEngine is null)
        {
            return new ActionResult
                   {
                       Success = false
                     , Message = "Insight engine is not configured in this context."
                     , Data    = Array.Empty<Insight>()
                   };
        }

        var context  = new ConversationContext("on-demand-insights");
        var insights = await _insightEngine.GenerateInsightsAsync(context).ConfigureAwait(false);

        if (insights is null || insights.Count == 0)
        {
            return new ActionResult
                   {
                       Success = true
                     , Message = "No new actionable insights generated at this time."
                     , Data    = Array.Empty<Insight>()
                   };
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Generated {insights.Count} insight(s):");

        foreach (var insight in insights)
        {
            sb.AppendLine();
            sb.AppendLine($"- **[{insight.Category}]** {insight.Message}");
        }

        return new ActionResult
               {
                   Success = true
                 , Message = sb.ToString()
                 , Data    = insights
               };
    }
}
