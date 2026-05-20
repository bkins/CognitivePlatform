using System.Text;
using CognitivePlatform.Api.Attributes;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Identity;

public class IdentityActions
{
    private readonly IIdentityService _identityService;

    public IdentityActions(IIdentityService identityService)
    {
        _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
    }

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
