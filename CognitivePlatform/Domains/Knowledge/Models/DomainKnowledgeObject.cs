using System;
using System.Collections.Generic;

namespace CognitivePlatform.Api.Domains.Knowledge.Models;

public sealed class DomainKnowledgeObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string IdString => Id.ToString();
    public string DomainName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public DateTimeOffset IngestedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<string> Tags { get; init; } = new();
}
