using System;

namespace CognitivePlatform.Api.Domains.Knowledge.Models;

public sealed class KnowledgeDomain
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public KnowledgeDomainMode Mode { get; init; } = KnowledgeDomainMode.Grounded;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
