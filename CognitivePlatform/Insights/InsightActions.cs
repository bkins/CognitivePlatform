using System;
using System.Text;
using System.Threading.Tasks;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Insights;

[Domain(typeof(WellbeingDomain))]
public sealed class InsightActions
{
    private readonly IInsightEngine _insightEngine;

    public InsightActions(IInsightEngine insightEngine)
    {
        _insightEngine = insightEngine ?? throw new ArgumentNullException(nameof(insightEngine));
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
