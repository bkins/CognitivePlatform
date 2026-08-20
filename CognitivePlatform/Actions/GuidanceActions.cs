using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry.Capabilities;
using CognitivePlatform.Api.Registry.Domains;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Actions;

/// <summary>
/// ENH-28 — Conversational capability guidance.
/// Explains a feature area in plain language instead of returning a raw action dump.
/// Surfaces how a domain works, what operations are available, and how it relates
/// to other features — useful for onboarding and returning users.
/// </summary>
[Domain(typeof(SystemDomain))]
public static class GuidanceActions
{
    private static ICapabilityRegistry? _registry;

    public static void SetRegistry( ICapabilityRegistry registry )
    {
        _registry = registry;
    }

    [NaturalLanguageAction(Description = "Explains how a specific feature area works, including available operations, example phrases, and relationships to other features."
                         , Examples =
                           [
                                   "Tell me about Tasks"
                                 , "How does the Journal work?"
                                 , "What can I do with my calendar?"
                                 , "Explain the Daily feature"
                                 , "How do Insights work?"
                           ]
                         , Category = "interpreter"
    )]
    public static string GetDomainGuidance( string domainName )
    {
        if (domainName.HasNoValue())
            return "Please tell me which feature area you'd like to know about. "
                 + "Try: \"Tell me about Tasks\", \"How does Journal work?\", or \"Explain Calendar\".";

        var allActions = _registry?.GetAll() ?? Array.Empty<ActionMetadata>();

        return BuildGuidanceResponse(domainName
                                   , allActions);
    }

    /// <summary>
    /// Builds a human-readable guidance response for the requested domain.
    /// Exposed as internal for unit testing without requiring a live registry.
    /// </summary>
    internal static string BuildGuidanceResponse( string                      domainName
                                                , IEnumerable<ActionMetadata> allActions )
    {
        var canonical = ResolveDomainAlias(domainName);

        if (canonical is null)
        {
            var knownNames = string.Join(", ", KnownDomainAliases.Values
                                                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                                                 .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            return $"I don't recognize \"{domainName}\" as a feature area. "
                 + $"Known areas: {knownNames}. "
                 + "Try: \"Tell me about Tasks\" or \"How does Journal work?\"";
        }

        var domainActions = allActions
                           .Where(action => (action.Domain?.Name ?? action.Category).EqualsIgnoreCase(canonical))
                           .OrderBy(action => action.Name
                                  , StringComparer.OrdinalIgnoreCase)
                           .ToList();

        var sb = new StringBuilder();

        sb.AppendLine($"## {canonical}");
        sb.AppendLine();

        var description = DomainDescriptions.GetValueOrDefault(canonical);
        if (description.HasValue())
        {
            sb.AppendLine(description);
            sb.AppendLine();
        }

        if (domainActions.Count > 0)
        {
            sb.AppendLine("**What you can do:**");
            foreach (var action in domainActions)
            {
                sb.Append($" - **{action.Name}**");

                if (action.Description.HasValue())
                {
                    sb.Append(" — ");
                    sb.Append(action.Description);
                }

                sb.AppendLine();

                if (action.Examples is not { Length: > 0 }) continue;

                foreach (var example in action.Examples.Take(2))
                    sb.AppendLine($"   _{example}_");
            }

            sb.AppendLine();
        }

        var relationships = DomainRelationships.GetValueOrDefault(canonical);
        if (relationships.HasValue())
        {
            sb.AppendLine("**How it connects to other features:**");
            sb.AppendLine(relationships);
            sb.AppendLine();
        }

        sb.Append("Ask \"Tell me about [feature]\" for any area, "
                + "or \"What can you do?\" for the full action list.");

        return sb.ToString();
    }

    // ================================================================
    // Domain alias map — maps user-facing terms to canonical domain names
    // ================================================================

    private static readonly Dictionary<string, string> KnownDomainAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                     {
                             "tasks", "Tasks"
                     }
                   , {
                             "task", "Tasks"
                     }
                   , {
                             "todo", "Tasks"
                     }
                   , {
                             "to-do", "Tasks"
                     }
                   , {
                             "journal", "Journal"
                     }
                   , {
                             "journaling", "Journal"
                     }
                   , {
                             "diary", "Journal"
                     }
                   , {
                             "daily", "Daily"
                     }
                   , {
                             "daily record", "Daily"
                     }
                   , {
                             "day", "Daily"
                     }
                   , {
                             "calendar", "Calendar"
                     }
                   , {
                             "schedule", "Calendar"
                     }
                   , {
                             "events", "Calendar"
                     }
                   , {
                             "knowledge", "Knowledge"
                     }
                   , {
                             "inbox", "Knowledge"
                     }
                   , {
                             "insights", "Knowledge"
                     }
                   , {
                             "identity", "Identity"
                     }
                   , {
                             "profile", "Identity"
                     }
                   , {
                             "system", "System"
                     }
                   , {
                             "help", "System"
                     }
                   , {
                             "meta", "System"
                     }
                   , {
                             "personality", "Personality"
                     }
                   , {
                             "tone", "Personality"
                     }
                   , {
                             "style", "Personality"
                     }
                   , {
                             "personas", "Personas"
                     }
                   , {
                             "persona", "Personas"
                     }
                   , {
                             "synthetic", "Personas"
                     }
                   , {
                             "personaengine", "PersonaEngine"
                     }
            };

    private static string? ResolveDomainAlias( string input )
    {
        var normalized = input.Trim();

        if (KnownDomainAliases.TryGetValue(normalized
                                         , out var canonical))
            return canonical;

        // Partial prefix match — "task management" → "Tasks"
        return (from kvp in KnownDomainAliases
                where normalized.StartsWithIgnoreCase(kvp.Key)
                select kvp.Value).FirstOrDefault();

    }

    // ================================================================
    // Authored domain descriptions (one paragraph each)
    // ================================================================

    private static readonly Dictionary<string, string> DomainDescriptions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                     {
                             "Tasks", "Tasks are your to-do list with built-in Eisenhower-matrix priority awareness. "
                                    + "Each task can have a description, urgency flag, importance flag, and due date. "
                                    + "You can add tasks individually, in batch, or as part of your Daily Plan."
                     }
                   , {
                             "Journal", "The Journal is an append-only log of your thoughts, reflections, and daily experiences. "
                                      + "Entries support mood tracking, tags, and revision history. "
                                      + "Every edit creates a new revision — the original is always preserved."
                     }
                   , {
                             "Daily", "Daily Records give your day structure: open it in the morning with a plan and tasks, "
                                    + "add checkpoints through the day, and close it in the evening with a wrap-up. "
                                    + "Unfinished tasks roll over to tomorrow automatically when you close the day."
                     }
                   , {
                             "Calendar", "Calendar connects to your Google Calendar account and lets you view events, check free time, "
                                       + "and add new events in natural language. "
                                       + "You can choose which calendars to include so noisy shared calendars don't clutter your view."
                     }
                   , {
                             "Knowledge", "The Knowledge Inbox surfaces patterns and insights generated by the Insight Engine "
                                        + "across all your domains — journals, tasks, and daily records. "
                                        + "Insights are advisory and confidence-based; you are never obligated to act on them."
                     }
                   , {
                             "Identity", "Your Identity profile is a structured, curated model of who you are: "
                                       + "preferred name, occupation, core values, personality traits, long-term goals, strengths, and stressors. "
                                       + "The system uses this profile to personalize insights and responses."
                     }
                   , {
                             "System", "System actions let you inspect the platform (version, environment, health), "
                                     + "switch LLM models and providers, list available capabilities, and get guidance on any feature."
                     }
                   , {
                             "Personality", "Personalities control how the assistant communicates — tone, style, and warmth. "
                                          + "Built-in options include Friendly Helper, Programmer, Witty, Zen, Tech Expert, and Motivational Coach. "
                                          + "You can add custom personalities via a JSON file."
                     }
                   , {
                             "Personas", "Personas are named synthetic people you create and have conversations with. "
                                       + "Each persona builds a memory over the conversation and can be loaded back in later sessions."
                     }
                   , {
                             "PersonaEngine", "The Persona Engine manages which persona is active in the current conversation. "
                                            + "It controls context-switching and maintains the active persona's conversational state."
                     }
            };

    // ================================================================
    // Authored cross-domain relationship descriptions
    // ================================================================

    private static readonly Dictionary<string, string> DomainRelationships =
            new(StringComparer.OrdinalIgnoreCase)
            {
                     {
                             "Tasks", "Tasks appear in your **Daily Plan** when you open the day. "
                                    + "The **Insight Engine** analyzes your task patterns to surface coaching suggestions in the Knowledge Inbox. "
                                    + "Overdue tasks are detected automatically and may trigger an Insight if you haven't journaled recently."
                     }
                   , {
                             "Journal", "The **Insight Engine** reads your recent journal entries to detect mood trends and stress patterns. "
                                      + "Journal entries feed into your **Identity** profile over time via derived insights. "
                                      + "The **Daily Record** is the structured counterpart — journals are free-form, daily records are structured."
                     }
                   , {
                             "Daily", "Tasks added in a Daily Plan appear in your **Tasks** list. "
                                    + "Closing the day rolls unfinished tasks forward. "
                                    + "The **Insight Engine** can factor daily-record patterns into its analysis."
                     }
                   , {
                             "Calendar", "Calendar is standalone today — events are not yet automatically linked to Tasks or Daily Records. "
                                       + "Free-time queries make it easy to find slots for scheduling tasks."
                     }
                   , {
                             "Knowledge", "Insights are generated from **Journal**, **Task**, and **Daily Record** data. "
                                        + "Your **Identity** profile feeds into the insight context so suggestions are personalized."
                     }
                   , {
                             "Identity", "The Identity profile is used by the **Insight Engine** to personalize suggestions. "
                                       + "Derived insights from your **Journal** history can be confirmed and promoted into your profile."
                     }
                   , {
                             "Personality", "Personality is independent of domain — it affects every response the assistant gives. "
                                          + "Switching personality does not change your data in Tasks, Journal, or any other domain."
                     }
            };
}
