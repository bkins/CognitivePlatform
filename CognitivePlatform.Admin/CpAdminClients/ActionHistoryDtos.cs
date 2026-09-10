namespace CognitivePlatform.Admin.CpAdminClients;

public sealed record ActionHistoryItemDto(string ActionName, string Outcome, DateTimeOffset OccurredUtc, string? ParameterSummary, string? ExecutionSummary);
