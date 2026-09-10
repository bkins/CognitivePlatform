namespace CognitivePlatform.Api.Domains.Personas.Models;

public sealed class PendingMemoryConfirmation
{
    public Guid     Id               { get; set; }
    public Guid     PersonaId        { get; set; }
    public Guid     PersonaMemoryId  { get; set; }
    public string   ConversationId   { get; set; } = string.Empty;
    public bool     IsResolved       { get; set; }
    public DateTime CreatedUtc       { get; set; }
    public DateTime? ResolvedUtc     { get; set; }
    public string?  ResolutionReason { get; set; }
}
