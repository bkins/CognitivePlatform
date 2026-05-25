namespace CognitivePlatform.Api.Registry.Domains;

public sealed record JournalDomain : IDomainDefinition
{
    public string Name        => "Journal";
    public string Description => "Append-only journal entries with mood tracking and revision history.";
}

public sealed record TasksDomain : IDomainDefinition
{
    public string Name        => "Tasks";
    public string Description => "To-do list with Eisenhower-matrix priority awareness.";
}

public sealed record DailyRecordDomain : IDomainDefinition
{
    public string Name        => "Daily";
    public string Description => "Daily structured log with open/close lifecycle, checkpoint tracking, and task rollover.";
}

public sealed record CalendarDomain : IDomainDefinition
{
    public string Name        => "Calendar";
    public string Description => "Calendar event management with multi-calendar support and free-time scheduling.";
}

public sealed record KnowledgeDomain : IDomainDefinition
{
    public string Name        => "Knowledge";
    public string Description => "Knowledge pattern analysis and cross-domain insight surfacing.";
}

public sealed record SystemDomain : IDomainDefinition
{
    public string Name        => "System";
    public string Description => "Platform infrastructure, system diagnostics, LLM configuration, and interpreter meta-actions.";
}

public sealed record PersonalityDomain : IDomainDefinition
{
    public string Name        => "Personality";
    public string Description => "Response personality and tone configuration for the assistant.";
}

public sealed record PersonaEngineDomain : IDomainDefinition
{
    public string Name        => "PersonaEngine";
    public string Description => "Active persona management and context switching for the assistant engine.";
}

public sealed record IdentityDomain : IDomainDefinition
{
    public string Name        => "Identity";
    public string Description => "User identity profile, behavioral assertions, and derived insight management.";
}

public sealed record PersonasDomain : IDomainDefinition
{
    public string Name        => "Personas";
    public string Description => "Named persona definition and persistent memory management.";
}
