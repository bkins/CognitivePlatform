using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Domains.Personality;

[Domain(typeof(PersonalityDomain))]
public class PersonalityActions
{
    private readonly IPersonalityService _personalityService;

    public PersonalityActions(IPersonalityService personalityService)
    {
        _personalityService = personalityService ?? throw new ArgumentNullException(nameof(personalityService));
    }

    [FastPath]
    [NaturalLanguageAction(Description = "Lists all available personalities with their name and description."
                         , Examples =
                           [
                                   "Show me the available personalities."
                                 , "List all personalities."
                                 , "What personalities are available?"
                                 , "Show personality options."
                           ]
                         , AllowsClarification = false)]
    public async Task<string> ListPersonalities()
    {
        var personalities = await _personalityService.GetAllAsync().ConfigureAwait(false);

        if (personalities.Count == 0)
            return "No personalities found.";

        var active = personalities.FirstOrDefault(personality => personality.IsActive);

        var sb = new StringBuilder();
        sb.AppendLine($"# Personalities ({personalities.Count})");
        sb.AppendLine();

        foreach (var personality in personalities)
        {
            var activeMark = personality.IsActive      ? " *(active)*" : string.Empty;
            var builtInTag = personality.IsBuiltIn     ? " [built-in]" : " [custom]";
            var modelNote  = personality.ModelConfig?.ModelId is not null
                                     ? $"  — Model: {personality.ModelConfig.ModelId}"
                                     : string.Empty;

            sb.AppendLine($"## {personality.Name}{activeMark}{builtInTag}");
            sb.AppendLine();
            sb.AppendLine($"{personality.Description}{modelNote}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    [FastPath]
    [NaturalLanguageAction(Description = "Returns the currently active personality."
                         , Examples =
                           [
                                   "What personality is active?"
                                 , "Which personality am I using?"
                                 , "Show the current personality."
                                 , "What's the active personality?"
                           ]
                         , AllowsClarification = false)]
    public async Task<string> GetActivePersonality()
    {
        var active = await _personalityService.GetActiveAsync().ConfigureAwait(false);

        if (active is null)
            return "No active personality is set.";

        var modelNote = active.ModelConfig?.ModelId is not null
                                ? $"\n- **Model:** {active.ModelConfig.ModelId}"
                                : string.Empty;

        return $"**Active Personality:** {active.Name}\n"
             + $"- **Description:** {active.Description}\n"
             + $"- **Type:** {(active.IsBuiltIn ? "Built-in" : "Custom")}"
             + modelNote;
    }

    [FastPath]
    [NaturalLanguageAction(Description = "Sets the active personality by name."
                         , Examples =
                           [
                                   "Switch to the Programmer personality."
                                 , "Use the Zen personality."
                                 , "Set personality to Witty."
                                 , "Change to Tech Expert."
                                 , "Activate Motivational Coach."
                           ]
                         , AllowsClarification = true, IsReplayable = true)]
    public async Task<string> SetPersonality( [NaturalLanguageParam(Description = "The name of the personality to activate (e.g. 'Zen', 'Programmer', 'Witty')."
                                                                   , Optional    = false
                                                                   , AllowEmpty  = false)]
                                              string name )
    {
        var personalities = await _personalityService.GetAllAsync().ConfigureAwait(false);

        var match = personalities.FirstOrDefault(personality =>
            string.Equals(personality.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var available = string.Join(", ", personalities.Select(personality => personality.Name));
            return $"No personality named '{name}' was found. Available: {available}.";
        }

        await _personalityService.SetActiveAsync(match.Id).ConfigureAwait(false);

        return $"Personality set to **{match.Name}** — {match.Description}.";
    }
}
