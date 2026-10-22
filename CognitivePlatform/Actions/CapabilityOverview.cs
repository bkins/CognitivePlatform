using System.Globalization;
using System.Text;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Actions;

/// <summary>Read-only, bounded presentation of registered capabilities; never an execution authority.</summary>
public static class CapabilityOverview
{
    private static int PreferredOrder(string category) => category.ToLowerInvariant() switch
    {
        "tasks" => 0
      , "journal" => 1
      , "dailyrecord" or "daily record" => 2
      , "calendar" => 3
      , "insights" => 4
      , "braindump" or "brain dump" => 5
      , _ => 6
    };

    public static string Build(IEnumerable<ActionMetadata> actions)
    {
        var groups = actions.Where(action => !string.IsNullOrWhiteSpace(action.Description))
                            .GroupBy(action => action.Domain?.Name ?? action.Category, StringComparer.OrdinalIgnoreCase)
                            .OrderBy(group => PreferredOrder(group.Key))
                            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                            .Take(6)
                            .ToArray();
        if (groups.Length == 0)
            return "Capability information is unavailable right now. Please try again later.";

        var text = new StringBuilder("Here are some areas I can help with:\n");
        foreach (var group in groups)
        {
            var heading = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(group.Key.ToLowerInvariant());
            var ordered = group.OrderBy(action => action.Name, StringComparer.Ordinal).ToArray();
            text.AppendLine().Append("**").Append(heading).AppendLine("**");
            foreach (var action in ordered.Take(2))
            {
                var description = action.Description.Replace('\r', ' ').Replace('\n', ' ');
                text.Append("- ").Append(description.Length <= 140 ? description : description[..137] + "...");
                if (action.IsDestructive) text.Append(" (requires confirmation)");
                text.AppendLine();
            }

            var examples = ordered.SelectMany(action => (action.Examples ?? [])
                                  .Where(example => !string.IsNullOrWhiteSpace(example) && example.Length <= 160)
                                  .Take(2)
                                  .Select(example => new { Text = example, Confirmation = action.IsDestructive }))
                                  .DistinctBy(example => example.Text, StringComparer.OrdinalIgnoreCase)
                                  .Take(2);
            foreach (var example in examples)
            {
                text.Append("Try: “").Append(example.Text).Append('”');
                if (example.Confirmation) text.Append(" (requires confirmation)");
                text.AppendLine();
            }
        }

        text.AppendLine().AppendLine("Some features may require setup or sign-in. Suggestions do not run any actions.")
            .Append("Which area would you like help with? You can also ask “List all commands” for the full directory.");
        return text.ToString();
    }
}
