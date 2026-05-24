namespace CognitivePlatform.Api.Domains.Personas.Models;

public class TimelineEvent
{
    public Guid    Id              { get; set; }
    public string  EventDate       { get; set; } = string.Empty;
    public string  Description     { get; set; } = string.Empty;
    public float   EmotionalWeight { get; set; }
    public bool    IsCanon         { get; set; }
}
