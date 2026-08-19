namespace CognitivePlatform.Api.Registry.Domains;

public sealed record JournalDomain : IDomainDefinition
{
    public string Name        => "Journal";
    public string Description => "Append-only journal entries with mood tracking and revision history.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "journal"
      , "entry"
      , "entries"
      , "mood"
      , "diary"
      , "wrote"
      , "journaled"
      , "reflection"
      , "revision"
    };
}

public sealed record TasksDomain : IDomainDefinition
{
    public string Name        => "Tasks";
    public string Description => "To-do list with Eisenhower-matrix priority awareness.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "task"
      , "tasks"
      , "todo"
      , "to-do"
      , "complete"
      , "done"
      , "priority"
      , "urgent"
      , "due"
      , "deadline"
      , "finish"
      , "add task"
    };
}

public sealed record DailyRecordDomain : IDomainDefinition
{
    public string Name        => "Daily";
    public string Description => "Daily structured log with open/close lifecycle, checkpoint tracking, and task rollover.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "daily"
      , "open day"
      , "close day"
      , "checkpoint"
      , "morning"
      , "evening"
      , "end of day"
      , "start of day"
      , "daily plan"
      , "rollover"
      , "open the day"
      , "close the day"
    };
}

public sealed record CalendarDomain : IDomainDefinition
{
    public string Name        => "Calendar";
    public string Description => "Calendar event management with multi-calendar support and free-time scheduling.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "calendar"
      , "schedule"
      , "meeting"
      , "meetings"
      , "appointment"
      , "event"
      , "events"
      , "busy"
      , "free time"
      , "book"
      , "availability"
      , "on my calendar"
    };
}

public sealed record KnowledgeDomain : IDomainDefinition
{
    public string Name        => "Knowledge";
    public string Description => "Knowledge pattern analysis and cross-domain insight surfacing.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "insight"
      , "insights"
      , "knowledge"
      , "inbox"
      , "pattern"
      , "patterns"
    };
}

public sealed record SystemDomain : IDomainDefinition
{
    public string Name        => "System";
    public string Description => "Platform infrastructure, system diagnostics, LLM configuration, and interpreter meta-actions.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "help"
      , "version"
      , "health"
      , "model"
      , "provider"
      , "capabilities"
      , "actions"
      , "settings"
      , "what can you do"
      , "list actions"
    };
}

public sealed record PersonalityDomain : IDomainDefinition
{
    public string Name        => "Personality";
    public string Description => "Response personality and tone configuration for the assistant.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "personality"
      , "tone"
      , "style"
      , "friendly"
      , "witty"
      , "zen"
      , "motivational"
      , "assistant style"
    };
}

public sealed record PersonaEngineDomain : IDomainDefinition
{
    public string Name        => "PersonaEngine";
    public string Description => "Active persona management and context switching for the assistant engine.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "active persona"
      , "persona context"
      , "switch persona"
      , "begin persona"
      , "end persona"
    };
}

public sealed record IdentityDomain : IDomainDefinition
{
    public string Name        => "Identity";
    public string Description => "User identity profile, behavioral assertions, and derived insight management.";

    public IReadOnlyList<string> Keywords => new[]
    {
        // Domain-level terms
        "identity"
      , "profile"
      , "who am i"
      , "about me"
      , "assertion"
      , "snapshot"
      , "behavioral"

        // Profile list-field phrases (the actual phrases users say to AddToProfileList /
        // RemoveFromProfileList — must match the full phrase, not just a root word, to
        // avoid collisions with PersonalityDomain ("personality") or TasksDomain ("goals"))
      , "personality traits"          // "add 'empathy' to my personality traits"
      , "core values"                 // "add 'integrity' to my core values"
      , "leadership"                  // "add 'servant leadership' to my leadership styles"
      , "long-term goals"             // "add X to my long-term goals"
      , "long term goals"             // no-hyphen variant
      , "communication preferences"   // "add X to my communication preferences"

        // Shorter aliases that also appear in action examples
      , "my traits"
      , "my values"
      , "my goals"
      , "strengths"
      , "stressors"
      , "occupation"                  // "update my occupation to Software Engineer"
    };
}

public sealed record PersonasDomain : IDomainDefinition
{
    public string Name        => "Personas";
    public string Description => "Named persona definition and persistent memory management.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "persona"
      , "personas"
      , "synthetic person"
      , "create persona"
      , "persona memory"
    };
}

public sealed record HealthDomain : IDomainDefinition
{
    public string Name        => "Health";
    public string Description => "Health and fitness data from your device.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "health"
      , "steps"
      , "sleep"
      , "heart rate"
      , "distance"
      , "fitness"
      , "calories"
      , "bpm"
      , "activity"
      , "walk"
      , "walked"
      , "run"
      , "exercise"
    };
}

public sealed record FileSyncDomain : IDomainDefinition
{
    public string Name        => "FileSync";
    public string Description => "Cross-device file synchronisation managed through natural language commands.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "file"
      , "files"
      , "sync"
      , "synchronise"
      , "synchronize"
      , "folder"
      , "copy"
      , "transfer"
      , "backup"
      , "document"
      , "documents"
      , "download"
      , "upload"
      , "in sync"
      , "phone files"
    };
}

public sealed record DocumentDomain : IDomainDefinition
{
    public string Name        => "Document";
    public string Description => "Indexing and semantic search over arbitrary files on disk.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "document"
      , "documents"
      , "file"
      , "files"
      , "index"
      , "indexed"
      , "pdf"
      , "markdown"
      , "text file"
    };
}

public sealed record SemanticSearchDomain : IDomainDefinition
{
    public string Name        => "Search";
    public string Description => "Semantic search across all domain content using vector embeddings.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "semantic"
      , "search"
      , "find"
      , "similar"
      , "related"
      , "about"
      , "written"
      , "notes"
    };
}

public sealed record CognitionDomain : IDomainDefinition
{
    public string Name        => "Cognition";
    public string Description => "Cognitive pattern awareness and reflective journaling insights.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "cognitive"
      , "distortion"
      , "thinking patterns"
      , "journaling patterns"
      , "all-or-nothing"
      , "catastrophising"
      , "reflection"
    };
}

public sealed record WellbeingDomain : IDomainDefinition
{
    public string Name        => "Wellbeing";
    public string Description => "Cross-domain wellbeing pattern analysis combining sleep, activity, tasks, and journal signals.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "wellbeing"
      , "well-being"
      , "wellness"
      , "patterns"
      , "trends"
      , "sleep health"
      , "energy"
      , "burnout"
      , "balance"
      , "correlation"
      , "health check"
    };
}

public sealed record BrainDumpDomain : IDomainDefinition
{
    public string Name        => "BrainDump";
    public string Description => "Guided therapeutic brain dump: structured mental unloading across 7 categories to reduce cognitive load.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "brain dump"
      , "braindump"
      , "guided journal"
      , "guided brain dump"
      , "mental dump"
      , "unload"
      , "avoidance"
      , "procrastination"
      , "guided journaling"
      , "mental load"
    };
}

public sealed record MediaDomain : IDomainDefinition
{
    public string Name        => "Media";
    public string Description => "Media attachment management: upload, list, and retrieve files attached to any knowledge item.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "media"
      , "attachment"
      , "attachments"
      , "photo"
      , "image"
      , "file attached"
      , "upload"
      , "attached files"
    };
}

public sealed record FoodDomain : IDomainDefinition
{
    public string Name        => "Food";
    public string Description => "Natural-language food logging and nutrition tracking.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "food"
      , "meal"
      , "meals"
      , "eat"
      , "ate"
      , "breakfast"
      , "lunch"
      , "dinner"
      , "snack"
      , "nutrition"
      , "calories"
      , "pizza"
      , "egg"
      , "bacon"
      , "coffee"
      , "banana"
      , "oatmeal"
      , "salmon"
      , "asparagus"
    };
}

public sealed record SecretsDomain : IDomainDefinition
{
    public string Name        => "Secrets";
    public string Description => "Encrypted secrets vault storing sensitive credentials and private data.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "secret"
      , "secrets"
      , "vault"
      , "password"
      , "passwords"
      , "credential"
      , "credentials"
      , "private"
      , "encrypt"
      , "encrypted"
      , "sensitive"
    };
}

public sealed record WatchListDomain : IDomainDefinition
{
    public string Name        => "WatchList";
    public string Description => "Track movies, TV shows, and streaming availability across platforms.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "watchlist"
      , "watch list"
      , "movie"
      , "movies"
      , "show"
      , "shows"
      , "series"
      , "streaming"
      , "netflix"
      , "prime video"
      , "hulu"
      , "disney"
      , "watched"
      , "watching"
      , "to watch"
    };
}
