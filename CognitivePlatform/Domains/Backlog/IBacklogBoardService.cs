namespace CognitivePlatform.Api.Domains.Backlog;

public interface IBacklogBoardService
{
    Task<BacklogBoardDto> GetBoardAsync(CancellationToken cancellationToken = default);
    Task<BacklogStoryDto> CreateStoryAsync(CreateBacklogStoryRequest request, CancellationToken cancellationToken = default);
    Task<BacklogStoryDto> UpdateStoryAsync(Guid storyId, UpdateBacklogStoryRequest request, CancellationToken cancellationToken = default);
    Task<BacklogStoryDto> MoveStoryAsync(Guid storyId, MoveBacklogStoryRequest request, CancellationToken cancellationToken = default);
    Task<BacklogStoryDto> ArchiveStoryAsync(Guid storyId, ArchiveBacklogStoryRequest request, CancellationToken cancellationToken = default);
    Task<BacklogStoryDto> UnarchiveStoryAsync(Guid storyId, string? actor = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BacklogStoryDto>> GetArchivedStoriesAsync(CancellationToken cancellationToken = default);
    Task<BacklogReferenceDto> CreateReferenceAsync(string referenceType, CreateBacklogReferenceRequest request, CancellationToken cancellationToken = default);
    Task<BacklogReferenceDto> UpdateReferenceAsync(string referenceType, string key, UpdateBacklogReferenceRequest request, CancellationToken cancellationToken = default);
    Task<BacklogIntakeDto> SubmitIntakeAsync(SubmitBacklogIntakeRequest request, CancellationToken cancellationToken = default);
    Task<BacklogStoryDto> PromoteIntakeAsync(Guid intakeId, PromoteBacklogIntakeRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BacklogIntakeDto>> GetIntakeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BacklogAuditEventDto>> GetHistoryAsync(Guid storyId, CancellationToken cancellationToken = default);
    Task<string> ExportMarkdownAsync(CancellationToken cancellationToken = default);
    Task<BacklogRecoveryBackup> CreateBackupAsync(CancellationToken cancellationToken = default);
    Task ResetAsync(BacklogRecoveryBackup backup, CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(BacklogRecoveryBackup backup, CancellationToken cancellationToken = default);
}
