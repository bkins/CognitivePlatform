using System.Collections.Generic;

namespace CognitivePlatform.Api.Contracts;

public class ActionMetadataDto
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public bool IsFastPath { get; init; }
    public bool IsDestructive { get; init; }
    public string[]? Examples { get; init; }
    public List<ActionParameterDto> Parameters { get; init; } = new();
}

public class ActionParameterDto
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
}
