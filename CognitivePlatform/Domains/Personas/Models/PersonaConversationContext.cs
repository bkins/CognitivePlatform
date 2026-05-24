namespace CognitivePlatform.Api.Domains.Personas.Models;

public class PersonaConversationContext
{
    public Guid                         PersonaId                   { get; set; }
    public string                       PersonaName                 { get; set; } = string.Empty;
    public string                       SystemPrompt                { get; set; } = string.Empty;
    public IReadOnlyList<PersonaMemory> RelevantMemories            { get; set; } = [];
    public PersonaConfiguration         Configuration               { get; set; } = PersonaConfiguration.Default;
    public bool                         IsResurrectionZoneViolation { get; set; }
    public string?                      PreferredModelName          { get; set; }
    public bool                         PromptAdapted               { get; set; }
    public ConversationMode             ActiveMode                  { get; set; } = ConversationMode.FreeConversation;
}
