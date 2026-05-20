namespace CognitivePlatform.Api.Domains.Identity;

public sealed record IdentityAssertion
{
    public string          Id             { get; init; } = Guid.NewGuid().ToString("N");
    public string          Topic          { get; init; } = string.Empty;
    public string          Statement      { get; init; } = string.Empty;
    public double          Confidence     { get; init; }
    public bool            UserConfirmed  { get; init; }
    public DateTime        FirstObserved  { get; init; }
    public DateTime        LastReinforced { get; init; }
    public bool            IsDeleted      { get; init; } = false;
    public DateTimeOffset? DeletedUtc     { get; init; }
}
