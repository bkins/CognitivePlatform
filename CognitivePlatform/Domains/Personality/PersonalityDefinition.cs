namespace CognitivePlatform.Api.Domains.Personality;

public class PersonalityDefinition
{
    public string  Id           { get; set; } = Guid.NewGuid().ToString("N");
    public string  Name         { get; set; } = string.Empty;
    public string  Description  { get; set; } = string.Empty;
    public string  SystemPrompt { get; set; } = string.Empty;
    public bool    IsBuiltIn    { get; set; }
    public bool    IsActive     { get; set; }
    public bool    IsDeleted    { get; set; }
    public DateTimeOffset? DeletedUtc  { get; set; }
    public DateTimeOffset  CreatedAt   { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset  UpdatedAt   { get; set; } = DateTimeOffset.UtcNow;

    public PersonalityModelConfig? ModelConfig { get; set; }
}
