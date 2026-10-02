namespace CognitivePlatform.Api.Companion;

public sealed record CompanionSearchRequest(string WorkspaceId, string Query, int Offset = 0);
