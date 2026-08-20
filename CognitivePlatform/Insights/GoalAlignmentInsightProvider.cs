using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Identity;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Interpreter;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Phase B (ENH-31) insight provider. Compares stated goals in PersonProfile.LongTermGoals
/// against recent task patterns (last 7 days) using an LLM call. Surfaces either a gap
/// ("you haven't worked on X in 7 days") or an alignment win ("3 completions align with X").
///
/// <para>
/// Trigger:          PersonProfile.LongTermGoals non-empty; LLM detects notable gap or win.
/// Message:          LLM-generated advisory observation (one per turn).
/// DeduplicationKey: goals.alignment.{goalSlug}
/// RepeatWindow:     48 hours default (enforced by InsightPolicy via the engine).
/// InsightCategory:  Wellbeing
/// </para>
/// </summary>
public sealed class GoalAlignmentInsightProvider : IInsightProvider
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private readonly IIdentityService                         _identityService;
    private readonly ITaskService                             _taskService;
    private readonly ILlmRouter                               _router;
    private readonly ILogger<GoalAlignmentInsightProvider>    _logger;

    public InsightCategory Category => InsightCategory.Wellbeing;

    public GoalAlignmentInsightProvider( IIdentityService                      identityService
                                       , ITaskService                           taskService
                                       , ILlmRouter                             router
                                       , ILogger<GoalAlignmentInsightProvider>  logger )
    {
        _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        _taskService     = taskService     ?? throw new ArgumentNullException(nameof(taskService));
        _router          = router          ?? throw new ArgumentNullException(nameof(router));
        _logger          = logger          ?? throw new ArgumentNullException(nameof(logger));
    }

    public async IAsyncEnumerable<Insight> GenerateAsync(
        ConversationContext                        context
      , [EnumeratorCancellation] CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var profile = await _identityService.GetProfileAsync(cancellationToken);

        if (profile.LongTermGoals.Count == 0)
            yield break;

        var sevenDaysAgo = DateTimeOffset.UtcNow.AddDays(-7);

        var recentCompleted = _taskService.GetCompleted()
            .Where(task => task.CompletedAt.HasValue && task.CompletedAt.Value >= sevenDaysAgo)
            .ToList();

        var activeTasks = _taskService.GetActive();

        var prompt   = BuildPrompt(profile.LongTermGoals, activeTasks, recentCompleted);
        var response = (await _router.SendAsync(prompt
                                              , context
                                              , complexity: TaskComplexity.Heavy
                                              , ct:         cancellationToken)).Content;

        var result = TryParseResponse(response);

        if (result is null || result.Type is "none" or "" || result.Message.HasNoValue())
            yield break;

        var goalSlug       = ComputeGoalSlug(result.Goal ?? string.Empty);
        var deduplicationKey = $"goals.alignment.{goalSlug}";

        yield return new Insight
                     {
                             Message          = result.Message
                           , DeduplicationKey = deduplicationKey
                           , Category         = InsightCategory.Wellbeing
                           , Priority         = InsightPriority.Normal
                           , Reasoning        = new InsightReasoning
                                               {
                                                       Explanation = $"Goal alignment {result.Type} detected for goal: {result.Goal}"
                                               }
                     };
    }

    private static string BuildPrompt(
        IReadOnlyList<string>       goals
      , IReadOnlyList<TaskItem>     activeTasks
      , IReadOnlyList<TaskItem>     recentCompleted )
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a goal alignment analyser for a personal productivity assistant.");
        sb.AppendLine();
        sb.AppendLine("User's stated long-term goals:");
        foreach (var goal in goals)
            sb.AppendLine($"  - {goal}");
        sb.AppendLine();

        if (activeTasks.Count > 0)
        {
            sb.AppendLine("Current open tasks:");
            foreach (var task in activeTasks.Take(15))
                sb.AppendLine($"  - {task.ShortDescription}");
            sb.AppendLine();
        }

        if (recentCompleted.Count > 0)
        {
            sb.AppendLine("Tasks completed in the last 7 days:");
            foreach (var task in recentCompleted.Take(15))
                sb.AppendLine($"  - {task.ShortDescription}");
            sb.AppendLine();
        }

        sb.AppendLine("""
            Analyse whether the user's recent activity aligns with their stated goals.
            Pick ONE goal to comment on — either a gap (no recent task aligns) or a win (2 or more completed tasks align).

            Output STRICT JSON only — no prose, no markdown fences, no commentary:
            {
              "type":    "gap" | "win" | "none",
              "goal":    "<the specific goal you're commenting on>",
              "message": "<one brief, conversational, non-judgmental observation>"
            }

            Rules:
            - If type is "gap": note the absence gently, suggest adding a task
            - If type is "win": celebrate the alignment briefly
            - If there is nothing noteworthy, output exactly: {"type":"none","goal":"","message":""}
            - Never lecture or nag
            - message must be 1–2 sentences max
            """);

        return sb.ToString();
    }

    private AlignmentResult? TryParseResponse(string raw)
    {
        if (raw.HasNoValue()) return null;

        var json = ExtractJsonObject(raw);
        if (json is null)
        {
            _logger.LogDebug("GoalAlignmentInsightProvider: no JSON object in response. Raw={Raw}", raw);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AlignmentResult>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "GoalAlignmentInsightProvider: malformed JSON. Raw={Raw}", raw);
            return null;
        }
    }

    private static string? ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        if (start < 0) return null;

        var depth = 0;
        for (var i = start; i < raw.Length; i++)
        {
            switch (raw[i])
            {
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0) return raw.Substring(start, i - start + 1);
                    break;
            }
        }

        return null;
    }

    private static string ComputeGoalSlug(string goal)
    {
        if (goal.HasNoValue()) return "unknown";

        var clean = new string(
            goal.ToLowerInvariant()
                .Replace(' ', '-')
                .Where(ch => char.IsLetterOrDigit(ch) || ch == '-')
                .ToArray());

        return clean.Length > 30 ? clean[..30] : clean;
    }

    private sealed class AlignmentResult
    {
        public string? Type    { get; set; }
        public string? Goal    { get; set; }
        public string? Message { get; set; }
    }
}
