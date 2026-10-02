namespace CognitivePlatform.Api.DocumentAnalysis;

public sealed record DocumentAnalysisRequest(int ProtocolVersion, string ClientRequestId, IReadOnlyList<DocumentSnapshot> Documents, string? Question = null);
