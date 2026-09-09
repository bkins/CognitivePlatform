namespace CognitivePlatform.Api.Domains.Backlog;

public sealed record BacklogStoryDto(
    Guid Id,
    string DisplayId,
    string Title,
    string Description,
    string ProjectKey,
    string AreaKey,
    string? StreamKey,
    string ItemTypeKey,
    string ColumnKey,
    string Rank,
    int Priority,
    long Revision,
    IReadOnlyDictionary<string, string> Properties);

public sealed record BacklogReferenceDto(string Key, string Name, int SortOrder, bool IsArchived = false);

public sealed record BacklogBoardDto(
    IReadOnlyList<BacklogStoryDto> Stories,
    IReadOnlyList<BacklogReferenceDto> Projects,
    IReadOnlyList<BacklogReferenceDto> Areas,
    IReadOnlyList<BacklogReferenceDto> Streams,
    IReadOnlyList<BacklogReferenceDto> Columns,
    IReadOnlyList<BacklogReferenceDto> ItemTypes,
    IReadOnlyList<BacklogReferenceDto> PropertyDefinitions);

public sealed record CreateBacklogStoryRequest(
    string ProjectKey,
    string AreaKey,
    string ItemTypeKey,
    string ColumnKey,
    string Title,
    string Description,
    int Priority,
    string? StreamKey,
    IReadOnlyDictionary<string, string>? Properties,
    string? Actor = null,
    string? DisplayId = null);

public sealed record UpdateBacklogStoryRequest(
    string Title,
    string Description,
    string ProjectKey,
    string AreaKey,
    string ItemTypeKey,
    int Priority,
    string? StreamKey,
    IReadOnlyDictionary<string, string>? Properties,
    long ExpectedRevision,
    string? Actor = null);

public sealed record MoveBacklogStoryRequest(
    string ColumnKey,
    string? BeforeStoryId,
    string? AfterStoryId,
    long ExpectedRevision,
    string? Actor = null);

public sealed record ArchiveBacklogStoryRequest(long ExpectedRevision, string? Actor = null);

public sealed record CreateBacklogReferenceRequest(string Key, string Name, int SortOrder, string? Actor = null);

public sealed record UpdateBacklogReferenceRequest(string Name, int SortOrder, bool IsArchived, string? Actor = null);

public sealed record SubmitBacklogIntakeRequest(string Kind, string Description, string Source, string? Actor = null);

public sealed record BacklogIntakeDto(Guid Id, string Kind, string Description, string Source, DateTimeOffset SubmittedAtUtc, string State, Guid? PromotedStoryId);

public sealed record PromoteBacklogIntakeRequest(CreateBacklogStoryRequest Story, string? Actor = null);

public sealed record BacklogAuditEventDto(Guid Id, string EntityType, string EntityId, string Action, string Actor, string CorrelationId, DateTimeOffset OccurredAtUtc, string BeforeJson, string AfterJson);

public sealed record BacklogMigrationReport(
    int SourceRows,
    int ImportedRows,
    int SkippedRows,
    IReadOnlyList<string> Exceptions,
    bool IsReconciled);

public sealed record RestoreLegacyBaselineRequest(string Confirmation);

public sealed record BacklogRecoveryBackup(string Path, DateTimeOffset CreatedAtUtc);

public sealed record BacklogRestoreReport(string BackupPath, BacklogMigrationReport Migration, bool RestoredFromBackup = false);

public sealed record LegacyReconciliationPreview(
    string SourceFingerprint,
    IReadOnlyList<LegacyReconciliationStoryPreview> Stories,
    IReadOnlyList<string> BoardOnlyDisplayIds);

public sealed record LegacyReconciliationStoryPreview(
    Guid StoryId,
    string DisplayId,
    long ExpectedRevision,
    int LegacyPriority,
    int BoardPriority,
    string? LegacyStreamName,
    string? BoardStreamKey,
    bool StreamDiffers,
    string LegacyColumnKey,
    string BoardColumnKey,
    bool TitleDiffers,
    bool DescriptionDiffers);

public sealed record LegacyReconciliationApplyRequest(
    string SourceFingerprint,
    IReadOnlyList<LegacyReconciliationSelection> Selections,
    string? Actor = null);

public sealed record LegacyReconciliationSelection(
    Guid StoryId,
    long ExpectedRevision,
    bool ApplyPriority,
    bool ApplyStream,
    bool ApplyColumn,
    bool ApplyTitle = false,
    bool ApplyDescription = false);

public sealed record LegacyReconciliationApplyResult(
    IReadOnlyList<LegacyReconciliationApplyOutcome> Outcomes);

public sealed record LegacyReconciliationApplyOutcome(
    string DisplayId,
    string Outcome,
    string Detail);
