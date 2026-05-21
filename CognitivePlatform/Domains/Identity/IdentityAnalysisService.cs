using System.Text;
using System.Text.Json;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Domains.Identity;

public class IdentityAnalysisService : IIdentityAnalysisService
{
    private readonly IIdentityService             _identityService;
    private readonly IJournalService              _journalService;
    private readonly ITaskService                 _taskService;
    private readonly ILlmClient                   _llmClient;
    private readonly ILogger<IdentityAnalysisService> _logger;

    public IdentityAnalysisService( IIdentityService              identityService
                                  , IJournalService               journalService
                                  , ITaskService                  taskService
                                  , ILlmClient                    llmClient
                                  , ILogger<IdentityAnalysisService> logger )
    {
        _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        _journalService  = journalService  ?? throw new ArgumentNullException(nameof(journalService));
        _taskService     = taskService     ?? throw new ArgumentNullException(nameof(taskService));
        _llmClient       = llmClient       ?? throw new ArgumentNullException(nameof(llmClient));
        _logger          = logger          ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<DerivedInsight>> GenerateInsightsAsync(
        string            partitionKey
      , CancellationToken ct )
    {
        var thirtyDaysAgo  = DateTimeOffset.UtcNow.AddDays(-30);
        var journalEntries = _journalService.ListEntries(fromUtc: thirtyDaysAgo);
        var activeTasks    = _taskService.GetActive();
        var completedTasks = _taskService.GetCompleted();
        var assertions     = await _identityService.GetAssertionsAsync(ct);

        var prompt = BuildInsightsPrompt(journalEntries, activeTasks, completedTasks, assertions);

        string rawResponse;
        try
        {
            var llmResponse = await _llmClient.SendAsync(prompt, cancellationToken: ct);
            rawResponse = llmResponse.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed during GenerateInsightsAsync");
            throw;
        }

        var insights = ParseInsightsResponse(rawResponse, journalEntries);

        foreach (var insight in insights)
            await _identityService.AddDerivedInsightAsync(insight, ct);

        return insights;
    }

    public async Task<PersonalitySnapshot> GenerateSnapshotAsync(
        string            partitionKey
      , CancellationToken ct )
    {
        var fourteenDaysAgo = DateTimeOffset.UtcNow.AddDays(-14);
        var profile         = await _identityService.GetProfileAsync(ct);
        var assertions      = await _identityService.GetAssertionsAsync(ct);
        var derivedInsights = await _identityService.GetDerivedInsightsAsync(ct);
        var journalEntries  = _journalService.ListEntries(fromUtc: fourteenDaysAgo);

        var prompt = BuildSnapshotPrompt(profile, assertions, derivedInsights, journalEntries);

        string rawResponse;
        try
        {
            var llmResponse = await _llmClient.SendAsync(prompt, cancellationToken: ct);
            rawResponse = llmResponse.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed during GenerateSnapshotAsync");
            throw;
        }

        var snapshot = ParseSnapshotResponse(rawResponse);

        if (!string.IsNullOrWhiteSpace(snapshot.NarrativeSummary))
            await _identityService.AddSnapshotAsync(snapshot, ct);

        return snapshot;
    }

    // ================================================================
    // Phase C — reflective intelligence
    // ================================================================

    public async Task<string> ReflectOnChangeAsync(
        string            partitionKey
      , TimeSpan          window
      , CancellationToken ct )
    {
        var allSnapshots = await _identityService.GetSnapshotsAsync(ct);
        var cutoff       = DateTime.UtcNow - window;

        var windowedSnapshots = allSnapshots
                               .Where(snapshot => snapshot.Timestamp >= cutoff)
                               .OrderBy(snapshot => snapshot.Timestamp)
                               .ToList();

        if (windowedSnapshots.Count < 2)
            return "Not enough snapshot history yet — try 'generate snapshot' to start building your timeline.";

        var earliest = windowedSnapshots.First();
        var latest   = windowedSnapshots.Last();

        var prompt = BuildReflectOnChangePrompt(earliest, latest);

        string rawResponse;
        try
        {
            var llmResponse = await _llmClient.SendAsync(prompt, cancellationToken: ct);
            rawResponse = llmResponse.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed during ReflectOnChangeAsync");
            throw;
        }

        return rawResponse.Trim();
    }

    public async Task<string> IdentifyPatternsAsync(
        string            partitionKey
      , CancellationToken ct )
    {
        var ninetyDaysAgo   = DateTimeOffset.UtcNow.AddDays(-90);
        var journalEntries  = _journalService.ListEntries(fromUtc: ninetyDaysAgo);
        var assertions      = await _identityService.GetAssertionsAsync(ct);
        var derivedInsights = await _identityService.GetDerivedInsightsAsync(ct);

        var confirmedAssertions = assertions.Where(assertion => assertion.UserConfirmed).ToList();
        var confirmedInsights   = derivedInsights.Where(insight => insight.UserConfirmed).ToList();

        var prompt = BuildIdentifyPatternsPrompt(journalEntries, confirmedAssertions, confirmedInsights);

        string rawResponse;
        try
        {
            var llmResponse = await _llmClient.SendAsync(prompt, cancellationToken: ct);
            rawResponse = llmResponse.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed during IdentifyPatternsAsync");
            throw;
        }

        return rawResponse.Trim();
    }

    public async Task<string> AnalyzeStressorsAsync(
        string            partitionKey
      , CancellationToken ct )
    {
        var profile        = await _identityService.GetProfileAsync(ct);
        var allSnapshots   = await _identityService.GetSnapshotsAsync(ct);
        var ninetyDaysAgo  = DateTimeOffset.UtcNow.AddDays(-90);
        var journalEntries = _journalService.ListEntries(fromUtc: ninetyDaysAgo);

        var prompt = BuildAnalyzeStressorsPrompt(profile, allSnapshots, journalEntries);

        string rawResponse;
        try
        {
            var llmResponse = await _llmClient.SendAsync(prompt, cancellationToken: ct);
            rawResponse = llmResponse.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed during AnalyzeStressorsAsync");
            throw;
        }

        return rawResponse.Trim();
    }

    public async Task<string> SummarizeGrowthAsync(
        string            partitionKey
      , CancellationToken ct )
    {
        var allSnapshots = await _identityService.GetSnapshotsAsync(ct);

        if (allSnapshots.Count == 0)
            return "No snapshot history yet — try 'generate snapshot' to start tracking your growth.";

        var recentSnapshots = allSnapshots
                             .OrderByDescending(snapshot => snapshot.Timestamp)
                             .Take(3)
                             .ToList();

        var prompt = BuildSummarizeGrowthPrompt(recentSnapshots);

        string rawResponse;
        try
        {
            var llmResponse = await _llmClient.SendAsync(prompt, cancellationToken: ct);
            rawResponse = llmResponse.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed during SummarizeGrowthAsync");
            throw;
        }

        return rawResponse.Trim();
    }

    public async Task<string> CompareSnapshotsAsync(
        string            partitionKey
      , DateTime          from
      , DateTime          to
      , CancellationToken ct )
    {
        var allSnapshots = await _identityService.GetSnapshotsAsync(ct);

        if (allSnapshots.Count < 2)
            return "Not enough snapshot history to compare — try 'generate snapshot' to build your timeline.";

        var fromSnapshot = allSnapshots.OrderBy(snapshot => Math.Abs((snapshot.Timestamp - from).Ticks)).First();
        var toSnapshot   = allSnapshots.OrderBy(snapshot => Math.Abs((snapshot.Timestamp - to).Ticks)).First();

        if (fromSnapshot.Id == toSnapshot.Id)
            return "The two dates refer to the same snapshot — try specifying a wider date range.";

        var prompt = BuildCompareSnapshotsPrompt(fromSnapshot, toSnapshot, from, to);

        string rawResponse;
        try
        {
            var llmResponse = await _llmClient.SendAsync(prompt, cancellationToken: ct);
            rawResponse = llmResponse.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed during CompareSnapshotsAsync");
            throw;
        }

        return rawResponse.Trim();
    }

    // ================================================================
    // Prompt builders
    // ================================================================

    private static string BuildInsightsPrompt(
        IReadOnlyList<JournalEntryWithRevision> journalEntries
      , IReadOnlyList<TaskItem>                 activeTasks
      , IReadOnlyList<TaskItem>                 completedTasks
      , IReadOnlyList<IdentityAssertion>        assertions )
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are analyzing a user's behavioral patterns from their journal entries and task history.");
        sb.AppendLine("Your goal is to identify patterns, tendencies, and recurring themes in their behavior.");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: Your output represents HYPOTHESES only — not confirmed truths.");
        sb.AppendLine("The user must explicitly confirm any insight before it is treated as ground truth.");
        sb.AppendLine();

        if (assertions.Count > 0)
        {
            sb.AppendLine("=== Confirmed identity facts ===");
            foreach (var assertion in assertions.Where(assertion => assertion.UserConfirmed))
                sb.AppendLine($"- [{assertion.Topic}] {assertion.Statement}");
            sb.AppendLine();
        }

        if (journalEntries.Count > 0)
        {
            sb.AppendLine("=== Recent journal entries (last 30 days) ===");
            foreach (var entry in journalEntries)
            {
                var date    = entry.Entry.CreatedUtc.ToString("yyyy-MM-dd");
                var text    = entry.LatestRevision.Text;
                var mood    = entry.LatestRevision.Mood;
                var moodStr = string.IsNullOrWhiteSpace(mood) ? string.Empty : $" [mood: {mood}]";
                sb.AppendLine($"[{date}]{moodStr} {entry.Entry.Id}: {text}");
            }
            sb.AppendLine();
        }

        if (activeTasks.Count > 0 || completedTasks.Count > 0)
        {
            sb.AppendLine("=== Tasks ===");
            foreach (var task in activeTasks)
                sb.AppendLine($"- [open] {task.Id}: {task.ShortDescription}");
            foreach (var task in completedTasks.Take(20))
                sb.AppendLine($"- [done] {task.Id}: {task.ShortDescription}");
            sb.AppendLine();
        }

        sb.AppendLine("Analyze the above data and identify 2 to 5 distinct behavioral patterns or insights.");
        sb.AppendLine();
        sb.AppendLine("Respond with ONLY a valid JSON object in this exact format — no markdown, no preamble:");
        sb.AppendLine("""
{
  "insights": [
    {
      "insightType": "kebab-case-label",
      "description": "Specific, evidence-based description of the pattern (1-2 sentences)",
      "confidence": 0.75,
      "sourceReferences": ["entry-or-task-id-1", "entry-or-task-id-2"]
    }
  ]
}
""");
        sb.AppendLine("Valid insightType examples: stress-response, work-pattern, leadership-tendency, communication-style, productivity-pattern, emotional-regulation");
        sb.AppendLine("confidence must be between 0.0 and 1.0");
        sb.AppendLine("sourceReferences must contain IDs from the data above that support the insight");

        return sb.ToString();
    }

    private static string BuildSnapshotPrompt(
        PersonProfile                           profile
      , IReadOnlyList<IdentityAssertion>        assertions
      , IReadOnlyList<DerivedInsight>           derivedInsights
      , IReadOnlyList<JournalEntryWithRevision> journalEntries )
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are generating a personality snapshot — a narrative overview of who this person is right now,");
        sb.AppendLine("synthesized from their confirmed profile, behavioral insights, and recent journal entries.");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: This is a synthesized view based on observed patterns. It is your best understanding,");
        sb.AppendLine("not confirmed fact. Write with appropriate epistemic humility.");
        sb.AppendLine();

        sb.AppendLine("=== Confirmed profile ===");
        if (!string.IsNullOrWhiteSpace(profile.PreferredName))
            sb.AppendLine($"Name: {profile.PreferredName}");
        if (!string.IsNullOrWhiteSpace(profile.Occupation))
            sb.AppendLine($"Occupation: {profile.Occupation}");
        if (profile.CoreValues.Count > 0)
            sb.AppendLine($"Core values: {string.Join(", ", profile.CoreValues)}");
        if (profile.Strengths.Count > 0)
            sb.AppendLine($"Strengths: {string.Join(", ", profile.Strengths)}");
        if (profile.Stressors.Count > 0)
            sb.AppendLine($"Known stressors: {string.Join(", ", profile.Stressors)}");
        sb.AppendLine();

        if (assertions.Count > 0)
        {
            sb.AppendLine("=== Confirmed identity assertions ===");
            foreach (var assertion in assertions.Where(assertion => assertion.UserConfirmed))
                sb.AppendLine($"- [{assertion.Topic}] {assertion.Statement}");
            sb.AppendLine();
        }

        if (derivedInsights.Count > 0)
        {
            sb.AppendLine("=== AI-derived behavioral insights ===");
            foreach (var insight in derivedInsights.Take(10))
            {
                var confirmedLabel = insight.UserConfirmed ? "[confirmed]" : "[unconfirmed]";
                sb.AppendLine($"- {confirmedLabel} [{insight.InsightType}] {insight.Description}");
            }
            sb.AppendLine();
        }

        if (journalEntries.Count > 0)
        {
            sb.AppendLine("=== Recent journal entries (last 14 days) ===");
            foreach (var entry in journalEntries)
            {
                var date = entry.Entry.CreatedUtc.ToString("yyyy-MM-dd");
                var mood = entry.LatestRevision.Mood;
                var moodStr = string.IsNullOrWhiteSpace(mood) ? string.Empty : $" [mood: {mood}]";
                sb.AppendLine($"[{date}]{moodStr} {entry.LatestRevision.Text}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Generate a personality snapshot as a valid JSON object — no markdown, no preamble:");
        sb.AppendLine("""
{
  "narrativeSummary": "2-4 paragraph narrative written in third person",
  "dominantThemes": ["theme1", "theme2", "theme3"],
  "activeStressors": ["stressor1", "stressor2"],
  "motivators": ["motivator1", "motivator2"],
  "observedStrengths": ["strength1", "strength2"]
}
""");
        sb.AppendLine("dominantThemes: 3-5 recurring patterns or concerns");
        sb.AppendLine("activeStressors: 2-4 current challenges or friction sources");
        sb.AppendLine("motivators: 2-4 things that energize and drive this person");
        sb.AppendLine("observedStrengths: 2-4 capabilities or qualities appearing consistently");

        return sb.ToString();
    }

    private static string BuildReflectOnChangePrompt(
        PersonalitySnapshot earliest
      , PersonalitySnapshot latest )
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are analyzing how a person has changed over time by comparing two personality snapshots.");
        sb.AppendLine("Write a narrative about the evolution — what shifted, grew, or receded between the two points.");
        sb.AppendLine();

        sb.AppendLine($"=== Earlier snapshot ({earliest.Timestamp:yyyy-MM-dd}) ===");
        AppendSnapshotFields(sb, earliest);
        sb.AppendLine();

        sb.AppendLine($"=== More recent snapshot ({latest.Timestamp:yyyy-MM-dd}) ===");
        AppendSnapshotFields(sb, latest);
        sb.AppendLine();

        sb.AppendLine("Write a 2-3 paragraph narrative in second person ('you have...', 'you seem to...').");
        sb.AppendLine("Focus on what has shifted — themes that emerged or faded, stressors that resolved or intensified, strengths that developed.");
        sb.AppendLine("Be specific and grounded in the data. Write with appropriate epistemic humility.");

        return sb.ToString();
    }

    private static string BuildIdentifyPatternsPrompt(
        IReadOnlyList<JournalEntryWithRevision> journalEntries
      , IReadOnlyList<IdentityAssertion>        confirmedAssertions
      , IReadOnlyList<DerivedInsight>           confirmedInsights )
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are analyzing a person's journal history, confirmed identity facts, and confirmed behavioral insights.");
        sb.AppendLine("Surface the most significant recurring patterns you observe across this data.");
        sb.AppendLine();

        if (confirmedAssertions.Count > 0)
        {
            sb.AppendLine("=== Confirmed identity facts ===");
            foreach (var assertion in confirmedAssertions)
                sb.AppendLine($"- [{assertion.Topic}] {assertion.Statement}");
            sb.AppendLine();
        }

        if (confirmedInsights.Count > 0)
        {
            sb.AppendLine("=== Confirmed behavioral insights ===");
            foreach (var insight in confirmedInsights)
                sb.AppendLine($"- [{insight.InsightType}] {insight.Description}");
            sb.AppendLine();
        }

        if (journalEntries.Count > 0)
        {
            sb.AppendLine("=== Recent journal entries (last 90 days) ===");
            foreach (var entry in journalEntries)
            {
                var date    = entry.Entry.CreatedUtc.ToString("yyyy-MM-dd");
                var mood    = entry.LatestRevision.Mood;
                var moodStr = string.IsNullOrWhiteSpace(mood) ? string.Empty : $" [mood: {mood}]";
                sb.AppendLine($"[{date}]{moodStr} {entry.LatestRevision.Text}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Write a bulleted list of 3-6 recurring patterns or tendencies.");
        sb.AppendLine("Format each as: '- **Pattern name**: description with evidence from the data'");
        sb.AppendLine("Write in second person ('you tend to...', 'you often...') with appropriate epistemic humility.");

        return sb.ToString();
    }

    private static string BuildAnalyzeStressorsPrompt(
        PersonProfile                           profile
      , IReadOnlyList<PersonalitySnapshot>      snapshots
      , IReadOnlyList<JournalEntryWithRevision> journalEntries )
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are analyzing the stress patterns and burnout cycles of a person.");
        sb.AppendLine("Synthesize the stressor data below into a narrative about what causes their burnout and stress.");
        sb.AppendLine();

        if (profile.Stressors.Count > 0)
        {
            sb.AppendLine("=== Known stressors from profile ===");
            foreach (var stressor in profile.Stressors)
                sb.AppendLine($"- {stressor}");
            sb.AppendLine();
        }

        var allActiveStressors = snapshots
                                .SelectMany(snapshot => snapshot.ActiveStressors)
                                .Where(stressor => !string.IsNullOrWhiteSpace(stressor))
                                .ToList();

        if (allActiveStressors.Count > 0)
        {
            sb.AppendLine("=== Active stressors across personality snapshots ===");

            var stressorGroups = allActiveStressors
                                .GroupBy(stressor => stressor, StringComparer.OrdinalIgnoreCase)
                                .OrderByDescending(group => group.Count());

            foreach (var group in stressorGroups)
                sb.AppendLine($"- {group.Key} (appeared {group.Count()} time{(group.Count() == 1 ? string.Empty : "s")})");

            sb.AppendLine();
        }

        var stressSignalWords = new[] { "stressed", "overwhelmed", "burnout", "exhausted", "pressure", "anxious", "frustrated", "stuck", "struggle" };

        var stressfulEntries = journalEntries
                              .Where(entry => stressSignalWords.Any(word =>
                                  entry.LatestRevision.Text.Contains(word, StringComparison.OrdinalIgnoreCase)))
                              .ToList();

        if (stressfulEntries.Count > 0)
        {
            sb.AppendLine("=== Journal entries containing stress signals ===");
            foreach (var entry in stressfulEntries.Take(10))
            {
                var date = entry.Entry.CreatedUtc.ToString("yyyy-MM-dd");
                sb.AppendLine($"[{date}] {entry.LatestRevision.Text}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Write a 2-3 paragraph narrative synthesizing the stress and burnout patterns.");
        sb.AppendLine("Identify root causes, recurring triggers, and any cycles you observe.");
        sb.AppendLine("Write in second person with appropriate epistemic humility.");

        return sb.ToString();
    }

    private static string BuildSummarizeGrowthPrompt(IReadOnlyList<PersonalitySnapshot> recentSnapshots)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are analyzing a person's recent growth by comparing their personality snapshots.");
        sb.AppendLine("Focus on positive delta — what has strengthened, what new capabilities or qualities have emerged.");
        sb.AppendLine();

        foreach (var snapshot in recentSnapshots.OrderBy(snapshot => snapshot.Timestamp))
        {
            sb.AppendLine($"=== Snapshot ({snapshot.Timestamp:yyyy-MM-dd}) ===");
            AppendSnapshotFields(sb, snapshot);
            sb.AppendLine();
        }

        sb.AppendLine("Write a 1-2 paragraph growth narrative in second person.");
        sb.AppendLine("Frame it as: what you have gotten better at, what has strengthened, and what positive shifts you've made.");
        sb.AppendLine("Be specific and encouraging, grounded in the data.");

        return sb.ToString();
    }

    private static string BuildCompareSnapshotsPrompt(
        PersonalitySnapshot fromSnapshot
      , PersonalitySnapshot toSnapshot
      , DateTime            requestedFrom
      , DateTime            requestedTo )
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are performing a side-by-side comparison of two personality snapshots.");
        sb.AppendLine("Narrate how this person has changed between the two points in time.");
        sb.AppendLine();

        sb.AppendLine($"=== Snapshot A — {fromSnapshot.Timestamp:yyyy-MM-dd} (nearest to requested {requestedFrom:yyyy-MM-dd}) ===");
        AppendSnapshotFields(sb, fromSnapshot);
        sb.AppendLine();

        sb.AppendLine($"=== Snapshot B — {toSnapshot.Timestamp:yyyy-MM-dd} (nearest to requested {requestedTo:yyyy-MM-dd}) ===");
        AppendSnapshotFields(sb, toSnapshot);
        sb.AppendLine();

        sb.AppendLine("Write a 2-3 paragraph comparison in second person.");
        sb.AppendLine("Describe what is the same, what has shifted, and what the overall trajectory suggests.");
        sb.AppendLine("Be specific about which themes, stressors, motivators, and strengths changed between the two points.");

        return sb.ToString();
    }

    private static void AppendSnapshotFields(StringBuilder sb, PersonalitySnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.NarrativeSummary))
            sb.AppendLine($"Narrative: {snapshot.NarrativeSummary}");
        if (snapshot.DominantThemes.Count > 0)
            sb.AppendLine($"Dominant themes: {string.Join(", ", snapshot.DominantThemes)}");
        if (snapshot.ActiveStressors.Count > 0)
            sb.AppendLine($"Active stressors: {string.Join(", ", snapshot.ActiveStressors)}");
        if (snapshot.Motivators.Count > 0)
            sb.AppendLine($"Motivators: {string.Join(", ", snapshot.Motivators)}");
        if (snapshot.ObservedStrengths.Count > 0)
            sb.AppendLine($"Observed strengths: {string.Join(", ", snapshot.ObservedStrengths)}");
    }

    // ================================================================
    // Response parsers
    // ================================================================

    private static IReadOnlyList<DerivedInsight> ParseInsightsResponse(
        string                                  rawResponse
      , IReadOnlyList<JournalEntryWithRevision> journalEntries )
    {
        var json = ExtractJson(rawResponse);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("insights", out var insightsArray)
             || insightsArray.ValueKind != JsonValueKind.Array)
                return [];

            var now     = DateTime.UtcNow;
            var results = new List<DerivedInsight>();

            foreach (var element in insightsArray.EnumerateArray())
            {
                var insightType      = element.TryGetProperty("insightType",      out var it)   ? it.GetString()  ?? string.Empty : string.Empty;
                var description      = element.TryGetProperty("description",      out var desc) ? desc.GetString() ?? string.Empty : string.Empty;
                var confidence       = element.TryGetProperty("confidence",       out var conf) && conf.TryGetDouble(out var confVal) ? confVal : 0.5;
                var sourceRefs       = ParseStringArray(element, "sourceReferences");

                if (string.IsNullOrWhiteSpace(insightType) || string.IsNullOrWhiteSpace(description))
                    continue;

                results.Add(new DerivedInsight
                            {
                                    Id               = Guid.NewGuid().ToString("N")
                                  , InsightType      = insightType.Trim()
                                  , Description      = description.Trim()
                                  , Confidence       = Math.Clamp(confidence, 0.0, 1.0)
                                  , SourceReferences = sourceRefs
                                  , GeneratedAt      = now
                                  , UserConfirmed    = false
                            });
            }

            return results;
        }
        catch
        {
            return [];
        }
    }

    private static PersonalitySnapshot ParseSnapshotResponse(string rawResponse)
    {
        var json = ExtractJson(rawResponse);
        if (string.IsNullOrWhiteSpace(json))
            return EmptySnapshot();

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var narrativeSummary  = root.TryGetProperty("narrativeSummary",  out var ns)  ? ns.GetString()  ?? string.Empty : string.Empty;
            var dominantThemes    = ParseStringArray(root, "dominantThemes");
            var activeStressors   = ParseStringArray(root, "activeStressors");
            var motivators        = ParseStringArray(root, "motivators");
            var observedStrengths = ParseStringArray(root, "observedStrengths");

            return new PersonalitySnapshot
                   {
                           Id                = Guid.NewGuid().ToString("N")
                         , Timestamp         = DateTime.UtcNow
                         , NarrativeSummary  = narrativeSummary.Trim()
                         , DominantThemes    = dominantThemes
                         , ActiveStressors   = activeStressors
                         , Motivators        = motivators
                         , ObservedStrengths = observedStrengths
                   };
        }
        catch
        {
            return EmptySnapshot();
        }
    }

    // ================================================================
    // Helpers
    // ================================================================

    private static string ExtractJson(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return string.Empty;

        // Strip markdown code fences if present (```json ... ``` or ``` ... ```)
        var trimmed = rawResponse.Trim();

        var fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var contentStart = trimmed.IndexOf('\n', fenceStart);
            var fenceEnd     = trimmed.LastIndexOf("```", StringComparison.Ordinal);

            if (contentStart >= 0 && fenceEnd > contentStart)
                return trimmed.Substring(contentStart, fenceEnd - contentStart).Trim();
        }

        // Find outermost JSON object boundaries
        var objectStart = trimmed.IndexOf('{');
        var objectEnd   = trimmed.LastIndexOf('}');

        if (objectStart >= 0 && objectEnd > objectStart)
            return trimmed.Substring(objectStart, objectEnd - objectStart + 1);

        return trimmed;
    }

    private static IReadOnlyList<string> ParseStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var arrayElement)
         || arrayElement.ValueKind != JsonValueKind.Array)
            return [];

        return arrayElement.EnumerateArray()
                           .Where(arrayItem => arrayItem.ValueKind == JsonValueKind.String)
                           .Select(arrayItem => arrayItem.GetString() ?? string.Empty)
                           .Where(value => !string.IsNullOrWhiteSpace(value))
                           .ToList();
    }

    private static PersonalitySnapshot EmptySnapshot()
        => new()
           {
                   Id               = Guid.NewGuid().ToString("N")
                 , Timestamp        = DateTime.UtcNow
                 , NarrativeSummary = string.Empty
           };
}
