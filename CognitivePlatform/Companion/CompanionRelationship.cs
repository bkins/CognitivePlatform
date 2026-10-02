namespace CognitivePlatform.Api.Companion;

public sealed record CompanionRelationship(Guid RelatedDocumentId, string Direction, string Kind, long Revision);
