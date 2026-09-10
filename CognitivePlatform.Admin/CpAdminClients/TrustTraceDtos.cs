namespace CognitivePlatform.Admin.CpAdminClients;

public sealed record TrustTraceItemDto(string Label, string Detail);
public sealed record TrustTraceDto(string Id, DateTimeOffset OccurredUtc, string? ActionName, bool Success, IReadOnlyList<TrustTraceItemDto> Items);
