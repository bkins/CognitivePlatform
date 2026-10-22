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
      , "daily" or "dailyrecord" or "daily record" => 2
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
            var names = group.Select(action => action.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ordered = group.Where(action => !action.Name.EndsWith("_Generated", StringComparison.OrdinalIgnoreCase)
                                            || !names.Contains(action.Name[..^"_Generated".Length]))
                               .OrderBy(action => action.Name, StringComparer.Ordinal)
                               .ToArray();
            var selected = ordered.Take(2).ToArray();
            text.AppendLine().Append("**").Append(heading).AppendLine("**");
            foreach (var action in selected)
            {
                var description = action.Description.Replace('\r', ' ').Replace('\n', ' ');
                text.Append("- ").Append(ShortDescription(description));
                if (action.IsDestructive) text.Append(" (requires confirmation)");
                text.AppendLine();
            }

            // Prefer one real example per displayed operation before taking alternatives.
            var examplesByAction = selected.Select(action => (action.Examples ?? [])
                                           .Where(example => !string.IsNullOrWhiteSpace(example) && example.Length <= 160)
                                           .Select(example => new { Text = example, Confirmation = action.IsDestructive })
                                           .Take(2).ToArray()).ToArray();
            var examples = examplesByAction.SelectMany(examples => examples.Take(1))
                                          .Concat(examplesByAction.SelectMany(examples => examples.Skip(1)))
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
    private static string ShortDescription(string description)
    {
        if (description.Length <= 140) return description;
        var preview = description[..137];
        var boundary = preview.LastIndexOf(' ');
        return boundary > 0
                   ? preview[..boundary].TrimEnd(',', ';', ':') + "..."
                   : "See the full command directory for details.";
    }

}
