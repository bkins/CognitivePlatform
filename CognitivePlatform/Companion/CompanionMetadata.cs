namespace CognitivePlatform.Api.Companion;

public sealed record CompanionMetadata(bool Available, string Status, string? Type, string[] Tags, string[] Aliases,
                                      long? Revision, DateTimeOffset ReadUtc, CompanionRelationship[] Relationships);
