using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Domains.Personality;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Domains.PersonaEngine;

/// <summary>
/// "Persona" vocabulary aliases for the Personality domain actions.
/// Delegates to IPersonalityService — the same underlying store and logic —
/// so users may use either "persona" or "personality" phrasing interchangeably.
/// </summary>
[Domain(typeof(PersonaEngineDomain))]
public class PersonaEngineActions
{
    private readonly IPersonalityService _personalityService;

    public PersonaEngineActions(IPersonalityService personalityService)
    {
        _personalityService = personalityService ?? throw new ArgumentNullException(nameof(personalityService));
    }

    [FastPath]
    [NaturalLanguageAction(Description = "Lists all available personas with their name and description."
                         , Examples =
                           [
                                   "Show me the available personas."
                                 , "List all personas."
                                 , "What personas are available?"
                                 , "Show persona options."
                           ]
                         , AllowsClarification = false)]
    public async Task<string> ListPersonas()
    {
        var personalities = await _personalityService.GetAllAsync().ConfigureAwait(false);

        if (personalities.Count == 0)
            return "No personas found.";

        var sb = new StringBuilder();
        sb.AppendLine($"# Personas ({personalities.Count})");
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
    [NaturalLanguageAction(Description = "Returns the currently active persona."
                         , Examples =
                           [
                                   "What persona is active?"
                                 , "Which persona am I using?"
                                 , "Show the current persona."
                                 , "What's the active persona?"
                           ]
                         , AllowsClarification = false)]
    public async Task<string> GetActivePersona()
    {
        var active = await _personalityService.GetActiveAsync().ConfigureAwait(false);

        if (active is null)
            return "No active persona is set.";

        var modelNote = active.ModelConfig?.ModelId is not null
                                ? $"\n- **Model:** {active.ModelConfig.ModelId}"
                                : string.Empty;

        return $"**Active Persona:** {active.Name}\n"
             + $"- **Description:** {active.Description}\n"
             + $"- **Type:** {(active.IsBuiltIn ? "Built-in" : "Custom")}"
             + modelNote;
    }

    [FastPath]
    [NaturalLanguageAction(Description = "Sets the active persona by name."
                         , Examples =
                           [
                                   "Set persona to Programmer."
                                 , "Use persona Zen."
                                 , "Change persona to Witty."
                                 , "Set my persona to Tech Expert."
                                 , "Switch persona to Motivational Coach."
                           ]
                         , AllowsClarification = true, IsReplayable = true)]
    public async Task<string> SetPersona( [NaturalLanguageParam(Description = "The name of the persona to activate (e.g. 'Zen', 'Programmer', 'Witty')."
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
            return $"No persona named '{name}' was found. Available: {available}.";
        }

        await _personalityService.SetActiveAsync(match.Id).ConfigureAwait(false);

        return $"Persona set to **{match.Name}** — {match.Description}.";
    }
}
