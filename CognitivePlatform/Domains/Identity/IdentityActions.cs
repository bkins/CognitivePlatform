using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Execution;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Identity;

public class IdentityActions : ISessionAware
{
    private readonly IIdentityService         _identityService;
    private readonly IIdentityAnalysisService _analysisService;
    private          ConversationContext?      _sessionContext;

    public IdentityActions( IIdentityService         identityService
                          , IIdentityAnalysisService analysisService )
    {
        _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
    }

    void ISessionAware.SetSessionContext(ConversationContext context) => _sessionContext = context;

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Returns the current personal profile: name, occupation, core values, traits, and goals."
          , Examples         =
            [
                    "Show my profile."
                  , "Get my identity profile."
                  , "What's my profile?"
                  , "Show who I am."
                  , "Display my personal profile."
            ]
          , Category         = "Identity"
          , AllowsClarification = false)]
    public async Task<string> GetProfile()
    {
        var profile = await _identityService.GetProfileAsync(CancellationToken.None);

        var sb = new StringBuilder();
        sb.AppendLine("# Personal Profile");
        sb.AppendLine();
        sb.AppendLine($"**Name:** {FormatScalar(profile.PreferredName)}");
        sb.AppendLine($"**Occupation:** {FormatScalar(profile.Occupation)}");
        sb.AppendLine($"**Narrative:** {FormatScalar(profile.NarrativeSummary)}");
        sb.AppendLine();
        sb.AppendLine($"**Core Values:** {FormatList(profile.CoreValues)}");
        sb.AppendLine($"**Personality Traits:** {FormatList(profile.PersonalityTraits)}");
        sb.AppendLine($"**Leadership Styles:** {FormatList(profile.LeadershipStyles)}");
        sb.AppendLine($"**Long-Term Goals:** {FormatList(profile.LongTermGoals)}");
        sb.AppendLine($"**Strengths:** {FormatList(profile.Strengths)}");
        sb.AppendLine($"**Stressors:** {FormatList(profile.Stressors)}");
        sb.AppendLine($"**Communication Preferences:** {FormatList(profile.CommunicationPreferences)}");

        return sb.ToString().TrimEnd();
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Sets a scalar field on the personal profile. Valid fields: PreferredName, Occupation, NarrativeSummary."
          , Examples         =
            [
                    "Set my name to Ben."
                  , "Update my occupation to Software Engineer."
                  , "Set my narrative summary to ..."
                  , "Change my preferred name to Ben."
            ]
          , Category         = "Identity"
          , AllowsClarification = true
          , IsReplayable     = true)]
    public async Task<string> SetProfileField(
        [NaturalLanguageParam(Description = "The field name to set: PreferredName, Occupation, or NarrativeSummary."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string field
      , [NaturalLanguageParam(Description = "The new value for the field."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string value)
    {
        var profile = await _identityService.GetProfileAsync(CancellationToken.None);
        value = value.Trim();

        var updated = field.ToLowerInvariant() switch
        {
                "preferredname" or "name" => profile with { PreferredName    = value }
              , "occupation"              => profile with { Occupation        = value }
              , "narrativesummary"        => profile with { NarrativeSummary = value }
              , _                         => null
        };

        if (updated is null)
            return $"Unknown field '{field}'. Valid scalar fields: PreferredName, Occupation, NarrativeSummary.";

        await _identityService.UpdateProfileAsync(updated, CancellationToken.None);

        return $"Profile field '{field}' set to '{value}'.";
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Appends an item to a list field on the personal profile. "
                             + "Valid fields: CoreValues, PersonalityTraits, LeadershipStyles, LongTermGoals, Strengths, Stressors, CommunicationPreferences."
          , Examples         =
            [
                    "Add 'Integrity' to my core values."
                  , "Add 'empathy' to my personality traits."
                  , "Add 'servant leadership' to my leadership styles."
                  , "Add 'build a scalable product' to my long-term goals."
                  , "Add 'systems thinking' to my strengths."
                  , "Add 'context-switching' to my stressors."
            ]
          , Category         = "Identity"
          , AllowsClarification = true
          , IsReplayable     = true)]
    public async Task<string> AddToProfileList(
        [NaturalLanguageParam(Description = "The list field to add to: CoreValues, PersonalityTraits, LeadershipStyles, "
                                          + "LongTermGoals, Strengths, Stressors, or CommunicationPreferences."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string field
      , [NaturalLanguageParam(Description = "The item to add."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string item)
    {
        var profile = await _identityService.GetProfileAsync(CancellationToken.None);
        item = item.Trim();

        var updated = ResolveListField(field, profile, existing => AppendToList(existing, item));

        if (updated is null)
            return $"Unknown list field '{field}'. {ListFieldHint}";

        await _identityService.UpdateProfileAsync(updated, CancellationToken.None);

        return $"Added '{item}' to {NormalizeFieldLabel(field)}.";
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Removes an item from a list field on the personal profile. "
                             + "Valid fields: CoreValues, PersonalityTraits, LeadershipStyles, LongTermGoals, Strengths, Stressors, CommunicationPreferences."
          , Examples         =
            [
                    "Remove 'Integrity' from my core values."
                  , "Remove 'empathy' from my personality traits."
                  , "Remove 'context-switching' from my stressors."
            ]
          , Category         = "Identity"
          , AllowsClarification = true
          , IsReplayable     = true)]
    public async Task<string> RemoveFromProfileList(
        [NaturalLanguageParam(Description = "The list field to remove from: CoreValues, PersonalityTraits, LeadershipStyles, "
                                          + "LongTermGoals, Strengths, Stressors, or CommunicationPreferences."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string field
      , [NaturalLanguageParam(Description = "The item to remove."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string item)
    {
        var profile = await _identityService.GetProfileAsync(CancellationToken.None);
        item = item.Trim();

        var updated = ResolveListField(field, profile, existing => RemoveFromList(existing, item));

        if (updated is null)
            return $"Unknown list field '{field}'. {ListFieldHint}";

        await _identityService.UpdateProfileAsync(updated, CancellationToken.None);

        return $"Removed '{item}' from {NormalizeFieldLabel(field)}.";
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Adds a new identity assertion: a confirmed fact about who the user is under a given topic."
          , Examples         =
            [
                    "Assert that under 'stress response' I tend to catastrophize timelines."
                  , "Add an assertion: leadership style is servant leadership."
                  , "Record that I'm an introvert under personality."
                  , "Note that I prioritize deep work over meetings under work preferences."
            ]
          , Category         = "Identity"
          , AllowsClarification = true
          , IsReplayable     = true)]
    public async Task<string> AddIdentityAssertion(
        [NaturalLanguageParam(Description = "The topic this assertion falls under, e.g. 'stress response', 'leadership style'."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string topic
      , [NaturalLanguageParam(Description = "The assertion statement, e.g. 'tends to catastrophize timelines under burnout'."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string statement)
    {
        var now = DateTime.UtcNow;

        var assertion = new IdentityAssertion
                        {
                                Id             = Guid.NewGuid().ToString("N")
                              , Topic          = topic.Trim()
                              , Statement      = statement.Trim()
                              , Confidence     = 1.0
                              , UserConfirmed  = true
                              , FirstObserved  = now
                              , LastReinforced = now
                        };

        await _identityService.AddAssertionAsync(assertion, CancellationToken.None);

        return $"Assertion added — {assertion.Topic}: {assertion.Statement}";
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Lists all active identity assertions grouped by topic."
          , Examples         =
            [
                    "Show my identity assertions."
                  , "List my assertions."
                  , "What identity facts are recorded?"
                  , "Show my recorded traits and behaviors."
            ]
          , Category         = "Identity"
          , AllowsClarification = false)]
    public async Task<string> ListIdentityAssertions()
    {
        var assertions = await _identityService.GetAssertionsAsync(CancellationToken.None);

        if (assertions.Count == 0)
            return "No identity assertions have been recorded yet.";

        var sb = new StringBuilder();
        sb.AppendLine("# Identity Assertions");
        sb.AppendLine();

        var grouped = assertions.GroupBy(assertion => assertion.Topic)
                                .OrderBy(topicGroup => topicGroup.Key);

        foreach (var topicGroup in grouped)
        {
            sb.AppendLine($"## {topicGroup.Key}");
            sb.AppendLine();

            foreach (var assertion in topicGroup)
            {
                var confirmedMarker = assertion.UserConfirmed ? "[confirmed]" : "[unconfirmed]";
                sb.AppendLine($"- {confirmedMarker} {assertion.Statement}");
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Confirms the most recent identity assertion for a given topic, marking it as user-verified."
          , Examples         =
            [
                    "Confirm the assertion about stress response."
                  , "Confirm my leadership style assertion."
                  , "Mark the 'work preferences' assertion as confirmed."
            ]
          , Category         = "Identity"
          , AllowsClarification = true)]
    public async Task<string> ConfirmIdentityAssertion(
        [NaturalLanguageParam(Description = "The topic of the assertion to confirm, e.g. 'stress response'."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string topic)
    {
        var assertions = await _identityService.GetAssertionsAsync(CancellationToken.None);

        var match = assertions
                    .Where(assertion => assertion.Topic.Equals(topic.Trim(), StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(assertion => assertion.LastReinforced)
                    .FirstOrDefault();

        if (match is null)
            return $"No assertion found for topic '{topic}'.";

        await _identityService.ConfirmAssertionAsync(match.Id, CancellationToken.None);

        return $"Confirmed: [{match.Topic}] {match.Statement}";
    }

    // ================================================================
    // AI synthesis actions
    // ================================================================

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Analyzes recent journal entries and tasks to discover behavioral patterns and generate AI-derived insights about the user."
          , Examples         =
            [
                    "Generate insights from my journals."
                  , "Analyze my behavioral patterns."
                  , "Discover patterns in my behavior."
                  , "Find patterns in my journals."
                  , "Analyze my journals for insights."
            ]
          , Category         = "Identity"
          , AllowsClarification = false)]
    public async Task<string> GenerateInsights()
    {
        var insights = await _analysisService.GenerateInsightsAsync(string.Empty, CancellationToken.None, _sessionContext);

        if (insights.Count == 0)
            return "No behavioral patterns could be identified from the available data. Try adding more journal entries.";

        var sb = new StringBuilder();
        sb.AppendLine($"I identified {insights.Count} behavioral pattern{(insights.Count == 1 ? string.Empty : "s")} from your recent journals and tasks:");
        sb.AppendLine();

        foreach (var insight in insights)
        {
            var confidencePct = (int)(insight.Confidence * 100);
            sb.AppendLine($"**[{insight.InsightType}]** {insight.Description} _(confidence: {confidencePct}%)_");
        }

        sb.AppendLine();
        sb.AppendLine("These are hypotheses, not confirmed facts. Say _'confirm insight [type]'_ to accept any of them.");

        return sb.ToString().TrimEnd();
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Generates a narrative personality snapshot synthesizing confirmed profile, behavioral insights, and recent journal entries."
          , Examples         =
            [
                    "Generate a personality snapshot."
                  , "Create a personality analysis."
                  , "Analyze who I am."
                  , "Generate my personality snapshot."
                  , "Create a snapshot of who I am."
            ]
          , Category         = "Identity"
          , AllowsClarification = false)]
    public async Task<string> GenerateSnapshot()
    {
        var snapshot = await _analysisService.GenerateSnapshotAsync(string.Empty, CancellationToken.None, _sessionContext);

        if (string.IsNullOrWhiteSpace(snapshot.NarrativeSummary))
            return "Could not generate a snapshot — not enough data available. Try adding journal entries and insights first.";

        var sb = new StringBuilder();
        sb.AppendLine("# Personality Snapshot");
        sb.AppendLine($"_Generated {snapshot.Timestamp:yyyy-MM-dd HH:mm} UTC_");
        sb.AppendLine();
        sb.AppendLine(snapshot.NarrativeSummary);

        if (snapshot.DominantThemes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"**Dominant themes:** {string.Join(", ", snapshot.DominantThemes)}");
        }

        if (snapshot.ActiveStressors.Count > 0)
            sb.AppendLine($"**Active stressors:** {string.Join(", ", snapshot.ActiveStressors)}");

        if (snapshot.Motivators.Count > 0)
            sb.AppendLine($"**Motivators:** {string.Join(", ", snapshot.Motivators)}");

        if (snapshot.ObservedStrengths.Count > 0)
            sb.AppendLine($"**Observed strengths:** {string.Join(", ", snapshot.ObservedStrengths)}");

        return sb.ToString().TrimEnd();
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Returns the most recent personality snapshot's narrative summary and themes."
          , Examples         =
            [
                    "Show my latest snapshot."
                  , "Get my personality snapshot."
                  , "Show my personality snapshot."
                  , "What's my latest snapshot?"
                  , "Show the latest personality analysis."
            ]
          , Category         = "Identity"
          , AllowsClarification = false)]
    public async Task<string> GetLatestSnapshot()
    {
        var snapshot = await _identityService.GetLatestSnapshotAsync(CancellationToken.None);

        if (snapshot is null)
            return "No personality snapshot exists yet. Say _'generate snapshot'_ to create one.";

        var sb = new StringBuilder();
        sb.AppendLine("# Latest Personality Snapshot");
        sb.AppendLine($"_Generated {snapshot.Timestamp:yyyy-MM-dd HH:mm} UTC_");
        sb.AppendLine();
        sb.AppendLine(snapshot.NarrativeSummary);

        if (snapshot.DominantThemes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"**Dominant themes:** {string.Join(", ", snapshot.DominantThemes)}");
        }

        if (snapshot.ActiveStressors.Count > 0)
            sb.AppendLine($"**Active stressors:** {string.Join(", ", snapshot.ActiveStressors)}");

        if (snapshot.Motivators.Count > 0)
            sb.AppendLine($"**Motivators:** {string.Join(", ", snapshot.Motivators)}");

        if (snapshot.ObservedStrengths.Count > 0)
            sb.AppendLine($"**Observed strengths:** {string.Join(", ", snapshot.ObservedStrengths)}");

        return sb.ToString().TrimEnd();
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Lists all AI-derived behavioral insights with their confirmation status and confidence scores."
          , Examples         =
            [
                    "List derived insights."
                  , "Show derived insights."
                  , "Show AI-generated insights."
                  , "What insights have been generated?"
                  , "Show generated insights."
            ]
          , Category         = "Identity"
          , AllowsClarification = false)]
    public async Task<string> ListDerivedInsights()
    {
        var insights = await _identityService.GetDerivedInsightsAsync(CancellationToken.None);

        if (insights.Count == 0)
            return "No derived insights yet. Say _'generate insights'_ to discover behavioral patterns.";

        var sb = new StringBuilder();
        sb.AppendLine("# Derived Behavioral Insights");
        sb.AppendLine();

        foreach (var insight in insights)
        {
            var confirmedMarker  = insight.UserConfirmed ? "[confirmed]" : "[unconfirmed]";
            var confidencePct    = (int)(insight.Confidence * 100);
            sb.AppendLine($"- {confirmedMarker} **[{insight.InsightType}]** {insight.Description} _{confidencePct}% confidence_");
        }

        return sb.ToString().TrimEnd();
    }

    [FastPath]
    [NaturalLanguageAction(
            Description      = "Confirms the most recent unconfirmed derived insight matching a given insight type or keyword, marking it as user-verified."
          , Examples         =
            [
                    "Confirm insight stress-response."
                  , "Confirm the work-pattern insight."
                  , "Accept the leadership insight."
                  , "Confirm derived insight productivity-pattern."
            ]
          , Category         = "Identity"
          , AllowsClarification = true)]
    public async Task<string> ConfirmDerivedInsight(
        [NaturalLanguageParam(Description = "The insight type or keyword matching the insight to confirm, e.g. 'stress-response', 'work-pattern'."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string topic)
    {
        var insights = await _identityService.GetDerivedInsightsAsync(CancellationToken.None);

        var match = insights
                    .Where(insight => insight.InsightType.Contains(topic.Trim(), StringComparison.OrdinalIgnoreCase)
                                   || insight.Description.Contains(topic.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Where(insight => insight.UserConfirmed.Not())
                    .OrderByDescending(insight => insight.GeneratedAt)
                    .FirstOrDefault();

        if (match is null)
            return $"No unconfirmed insight found matching '{topic}'.";

        await _identityService.ConfirmDerivedInsightAsync(match.Id, CancellationToken.None);

        return $"Confirmed insight: [{match.InsightType}] {match.Description}";
    }

    // --- Private helpers ----------------------------------------------------

    private static PersonProfile? ResolveListField(
        string                                              field
      , PersonProfile                                      profile
      , Func<IReadOnlyList<string>, IReadOnlyList<string>> transform)
    {
        return field.ToLowerInvariant() switch
        {
                "corevalues"   or "values"               => profile with { CoreValues               = transform(profile.CoreValues) }
              , "personalitytraits" or "traits"           => profile with { PersonalityTraits        = transform(profile.PersonalityTraits) }
              , "leadershipstyles" or "leadership"        => profile with { LeadershipStyles         = transform(profile.LeadershipStyles) }
              , "longtermgoals" or "goals"                => profile with { LongTermGoals            = transform(profile.LongTermGoals) }
              , "strengths"                               => profile with { Strengths                = transform(profile.Strengths) }
              , "stressors"                               => profile with { Stressors                = transform(profile.Stressors) }
              , "communicationpreferences" or "communication" => profile with { CommunicationPreferences = transform(profile.CommunicationPreferences) }
              , _                                         => null
        };
    }

    private static IReadOnlyList<string> AppendToList(IReadOnlyList<string> existing, string item)
    {
        if (existing.Any(existingItem => existingItem.Equals(item, StringComparison.OrdinalIgnoreCase)))
            return existing;

        return existing.Append(item).ToList();
    }

    private static IReadOnlyList<string> RemoveFromList(IReadOnlyList<string> existing, string item)
    {
        return existing
               .Where(existingItem => existingItem.Equals(item, StringComparison.OrdinalIgnoreCase).Not())
               .ToList();
    }

    private static string FormatScalar(string value)
        => string.IsNullOrWhiteSpace(value) ? "_Not set_" : value;

    private static string FormatList(IReadOnlyList<string> items)
        => items.Count > 0 ? string.Join(", ", items) : "_None_";

    private static string NormalizeFieldLabel(string field) => field.ToLowerInvariant() switch
    {
            "corevalues"   or "values"               => "Core Values"
          , "personalitytraits" or "traits"           => "Personality Traits"
          , "leadershipstyles" or "leadership"        => "Leadership Styles"
          , "longtermgoals" or "goals"                => "Long-Term Goals"
          , "strengths"                               => "Strengths"
          , "stressors"                               => "Stressors"
          , "communicationpreferences" or "communication" => "Communication Preferences"
          , _                                         => field
    };

    private const string ListFieldHint =
        "Valid list fields: CoreValues, PersonalityTraits, LeadershipStyles, "
      + "LongTermGoals, Strengths, Stressors, CommunicationPreferences.";
}
