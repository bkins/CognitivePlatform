namespace CognitivePlatform.Api.Domains.Personas;

public sealed record PendingMemoryConfirmationSummary(int PendingCount, DateTimeOffset UpdatedUtc);
