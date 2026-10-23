using System.Text.RegularExpressions;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Conversation;

/// <summary>Bounded organization discovery; every action still goes through ordinary resolution/execution.</summary>
public static class OrganizationClarification
{
    private static string Normalize(string input) =>
        Regex.Replace(input.Trim().TrimEnd('?', '!', '.'), @"\s+", " ").Replace('’', '\'').ToLowerInvariant();

    private static string? IntentFamily(string normalized)
    {
        if (normalized is "help me get organized" or "i need help getting organized" or "help me organize my day") return "organization";
        if (Regex.IsMatch(normalized, @"^(?:i(?:'m| am)(?: feeling)? overwhelmed|i(?:'ve| have) got too much to do|i have too much to do|i don'?t know where to start)$")) return "overwhelm";
        if (Regex.IsMatch(normalized, @"^(?:(?:i need|i want) to plan tomorrow|(?:can|could) you help me plan tomorrow|help me (?:plan|prepare for) tomorrow)$")) return "future-day";
        return null;
    }

    public static bool IsStarter(string input) => IntentFamily(Normalize(input)) is not null;

    public static OrganizationClarificationDecision Evaluate(string input, ConversationContext context,
        IReadOnlyCollection<ActionMetadata> actions, IFastPathResolver fastPath)
    {
        if (context.PendingAction is not null)
        {
            context.PendingOrganizationIntent = null;
            return new();
        }

        var normalized = Normalize(input);
        var family = IntentFamily(normalized);
        var isStarter = family is not null;
        if (!isStarter && context.PendingOrganizationIntent is null) return new();
        var names = actions.Select(action => action.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var available = new List<string>();
        if (names.Contains("ListTasks")) available.Add("tasks");
        if (names.Contains("OpenDay") && names.Contains("GetDomainGuidance")) available.Add("daily planning");
        if (names.Contains("StartBrainDump") && names.Contains("GetDomainGuidance")) available.Add("brain dump");

        if (family == "future-day" || (!isStarter && context.PendingOrganizationIntent?.TargetDay == "tomorrow"))
            available.Remove("brain dump");

        if (isStarter)
        {
            if (available.Count == 0)
            {
                context.PendingOrganizationIntent = null;
                return new("I don't have an available organization option in the current catalog. You can ask what I can help with.");
            }
            var intent = new PendingOrganizationIntent
                         {
                             OriginalInput = input
                           , OfferedAreas = available.ToArray()
                           , IntentFamily = family!
                           , TargetDay = family == "future-day" ? "tomorrow" : null
                         };
            context.PendingOrganizationIntent = intent;
            return new(Question(available, intent));
        }

        var pending = context.PendingOrganizationIntent;
        if (pending is null) return new();
        if (normalized is "cancel" or "never mind" or "nevermind" or "stop")
        {
            context.PendingOrganizationIntent = null;
            return new("Cancelled getting-organized guidance. No action was executed.");
        }

        if (available.Count == 0) return Retry(context, pending, available, "No organization option remains available. ");

        var answer = Regex.Replace(normalized, @"^(?:actually|instead|no)[, ]+", "");
        if (pending.TargetDay == "tomorrow" && Regex.IsMatch(answer,
            @"^(?:please )?(?:(?:can|could) you )?(?:open|start|create) (?:tomorrow(?:'s)? (?:daily )?(?:plan|record)|(?:a |my )?daily (?:plan|record) for tomorrow)$"))
            return new(Question(available, pending));

        var area = answer switch
        {
            "tasks" or "my tasks" => "tasks",
            "daily" or "daily plan" or "daily planning" => "daily planning",
            "brain dump" or "braindump" => "brain dump",
            _ => null
        };
        if (area is not null)
        {
            if (!pending.OfferedAreas.Contains(area) || !available.Contains(area))
                return Retry(context, pending, available, "That option isn't available in this catalog. ");
            pending.SelectedArea = area;
            pending.UnclearReplies = 0;
            var dayContext = pending.TargetDay == "tomorrow" ? "For tomorrow, Daily opens today; this choice explains the available features. " : string.Empty;
            return new(dayContext + (area == "tasks"
                ? names.Contains("GetDomainGuidance")
                    ? "For your tasks, would you like to review active tasks or learn about task features?"
                    : "For your tasks, would you like to review active tasks? You can also cancel."
                : $"For {area}, would you like me to explain the available features? Say 'explain it', choose another area, or cancel."));
        }

        // A new explicit command supersedes the question. The existing engine owns its safety gates.
        if (fastPath.TryResolve(input, out _, out _))
        {
            context.PendingOrganizationIntent = null;
            return new();
        }

        if ((pending.SelectedArea == "tasks" && answer is "review active tasks" or "view active tasks" or "review them")
            || (pending.TargetDay == "tomorrow" && answer is "review active tasks" or "view active tasks"))
        {
            if (!names.Contains("ListTasks")) return Retry(context, pending, available, "Task listing is no longer available. ");
            context.PendingOrganizationIntent = null;
            return new(ResolveInput: "Show my active tasks");
        }
        if (pending.TargetDay == "tomorrow" && pending.SelectedArea is null && answer is "explain it" or "explain planning features")
            pending.SelectedArea = "daily planning";
        var wantsExplanation = answer is "explain it" or "learn about task features" or "explain the features" or "explain planning features"
                            || (pending.SelectedArea != "tasks" && answer is "yes" or "yes please" or "please");
        if (pending.SelectedArea is not null && wantsExplanation)
        {
            if (!names.Contains("GetDomainGuidance") || !available.Contains(pending.SelectedArea)) return Retry(context, pending, available, "Feature guidance isn't available. ");
            var domain = pending.SelectedArea switch { "tasks" => "Tasks", "daily planning" => "Daily", _ => "BrainDump" };
            context.PendingOrganizationIntent = null;
            return new(ResolveInput: $"Tell me about {domain}");
        }

        // Do not trap a new full request in this bounded short-answer flow.
        if (normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 4 || normalized.StartsWith("write ", StringComparison.Ordinal))
        {
            context.PendingOrganizationIntent = null;
            return new();
        }
        return Retry(context, pending, available, string.Empty);
    }

    private static OrganizationClarificationDecision Retry(ConversationContext context, PendingOrganizationIntent pending,
        IReadOnlyCollection<string> available, string prefix)
    {
        pending.UnclearReplies++;
        if (pending.UnclearReplies >= 2 || available.Count == 0)
        {
            context.PendingOrganizationIntent = null;
            return new(prefix + "I'll stop this clarification rather than guess. Ask for a specific task or feature when you're ready.");
        }
        return new(prefix + Question(available, pending));
    }

    private const string TomorrowBoundary = "Daily opens today's record; I can't open tomorrow's Daily Plan. ";

    private static string Question(IEnumerable<string> areas, PendingOrganizationIntent pending)
    {
        if (pending.TargetDay == "tomorrow")
        {
            var choices = new List<string>();
            if (areas.Contains("tasks")) choices.Add("review all active tasks as preparation");
            if (areas.Contains("daily planning")) choices.Add("explain planning features");
            return "For tomorrow, you can " + string.Join(" or ", choices) + ". "
                 + TomorrowBoundary + "You can also cancel. No action has been executed.";
        }
        return (pending.IntentFamily == "overwhelm" ? "That sounds like a lot. " : string.Empty)
             + "What would help you get organized: " + string.Join(", ", areas)
             + "? Choose an area, or say cancel. No action has been executed.";
    }
}
