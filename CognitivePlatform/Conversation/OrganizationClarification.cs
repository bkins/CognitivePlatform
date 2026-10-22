using System.Text.RegularExpressions;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Conversation;

/// <summary>Bounded organization discovery; every action still goes through ordinary resolution/execution.</summary>
public static class OrganizationClarification
{
    public static OrganizationClarificationDecision Evaluate(string input, ConversationContext context,
        IReadOnlyCollection<ActionMetadata> actions, IFastPathResolver fastPath)
    {
        if (context.PendingAction is not null)
        {
            context.PendingOrganizationIntent = null;
            return new();
        }

        var normalized = Regex.Replace(input.Trim().TrimEnd('?', '!', '.'), @"\s+", " ").ToLowerInvariant();
        var isStarter = normalized is "help me get organized" or "i need help getting organized" or "help me organize my day";
        if (!isStarter && context.PendingOrganizationIntent is null) return new();
        var names = actions.Select(action => action.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var available = new List<string>();
        if (names.Contains("ListTasks")) available.Add("tasks");
        if (names.Contains("OpenDay") && names.Contains("GetDomainGuidance")) available.Add("daily planning");
        if (names.Contains("StartBrainDump") && names.Contains("GetDomainGuidance")) available.Add("brain dump");

        if (isStarter)
        {
            if (available.Count == 0)
            {
                context.PendingOrganizationIntent = null;
                return new("I don't have an available organization option in the current catalog. You can ask what I can help with.");
            }
            context.PendingOrganizationIntent = new PendingOrganizationIntent { OriginalInput = input, OfferedAreas = available.ToArray() };
            return new(Question(available));
        }

        var pending = context.PendingOrganizationIntent;
        if (pending is null) return new();
        if (normalized is "cancel" or "never mind" or "nevermind" or "stop")
        {
            context.PendingOrganizationIntent = null;
            return new("Cancelled getting-organized guidance. No action was executed.");
        }

        var answer = Regex.Replace(normalized, @"^(?:actually|instead|no)[, ]+", "");
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
            return new(area == "tasks"
                ? names.Contains("GetDomainGuidance")
                    ? "For your tasks, would you like to review active tasks or learn about task features?"
                    : "For your tasks, would you like to review active tasks? You can also cancel."
                : $"For {area}, would you like me to explain the available features? Say 'explain it', choose another area, or cancel.");
        }

        // A new explicit command supersedes the question. The existing engine owns its safety gates.
        if (fastPath.TryResolve(input, out _, out _))
        {
            context.PendingOrganizationIntent = null;
            return new();
        }

        if (pending.SelectedArea == "tasks" && answer is "review active tasks" or "view active tasks")
        {
            if (!names.Contains("ListTasks")) return Retry(context, pending, available, "Task listing is no longer available. ");
            context.PendingOrganizationIntent = null;
            return new(ResolveInput: "Show my active tasks");
        }
        var wantsExplanation = answer is "explain it" or "learn about task features" or "explain the features"
                            || (pending.SelectedArea != "tasks" && answer is "yes" or "yes please" or "please");
        if (pending.SelectedArea is not null && wantsExplanation)
        {
            if (!names.Contains("GetDomainGuidance")) return Retry(context, pending, available, "Feature guidance isn't available. ");
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
        return new(prefix + Question(available));
    }

    private static string Question(IEnumerable<string> areas) =>
        "What would help you get organized: " + string.Join(", ", areas) + "? Choose an area, or say cancel. No action has been executed.";
}
