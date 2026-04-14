namespace CognitivePlatform.Admin.CpAdminClients;

/// <summary>Response from GET /api/admin/registry.</summary>
public sealed record ActionMetadataDto
{
    public string                              Name                { get; init; } = string.Empty;
    public string                              Description         { get; init; } = string.Empty;
    public string                              Category            { get; init; } = string.Empty;
    public string[]                            Examples            { get; init; } = [];
    public bool                                IsFastPath          { get; init; }
    public bool                                IsDestructive       { get; init; }
    public bool                                AllowsClarification { get; init; }
    public IReadOnlyList<ParameterMetadataDto> Parameters          { get; init; } = [];
}

public sealed record ParameterMetadataDto
{
    public string  Name         { get; init; } = string.Empty;
    public string  TypeName     { get; init; } = string.Empty;
    public string  Description  { get; init; } = string.Empty;
    public bool    IsOptional   { get; init; }
    public bool    AllowEmpty   { get; init; }
    public string? DefaultValue { get; init; }
}

/// <summary>Simplified response from POST /api/conversation/converse used by the test-invoke panel.</summary>
public sealed record ConverseResultDto
{
    public bool    Success        { get; init; }
    public string? Message        { get; init; }
    public string? SelectedAction { get; init; }
    public string? ExecutionResult { get; init; }
    public bool    WasFastPath    { get; init; }
}
