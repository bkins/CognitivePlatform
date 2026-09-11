using CognitivePlatform.Api.Domains.Backlog;

namespace CognitivePlatform.Admin.CpAdminClients;

public interface IAdminBacklogClient
{
    Task<BacklogBoardDto> GetBoardAsync(CancellationToken cancellationToken = default);
    Task<BacklogStoryDto> CreateStoryAsync(CreateBacklogStoryRequest request, CancellationToken cancellationToken = default);
    Task<BacklogStoryDto> UpdateStoryAsync(Guid storyId, UpdateBacklogStoryRequest request, CancellationToken cancellationToken = default);
    Task<BacklogStoryDto> MoveStoryAsync(Guid storyId, MoveBacklogStoryRequest request, CancellationToken cancellationToken = default);
    Task<BacklogStoryDto> ArchiveStoryAsync(Guid storyId, ArchiveBacklogStoryRequest request, CancellationToken cancellationToken = default);
    Task<BulkArchivePreview> PreviewBulkArchiveAsync(BulkArchivePreviewRequest request, CancellationToken cancellationToken = default);
    Task<BulkArchiveResult> ArchiveCompletedStoriesAsync(BulkArchiveExecuteRequest request, CancellationToken cancellationToken = default);
    Task<BacklogStoryDto> UnarchiveStoryAsync(Guid storyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BacklogStoryDto>> GetArchivedStoriesAsync(CancellationToken cancellationToken = default);
    Task<BacklogReferenceDto> CreateReferenceAsync(string referenceType, CreateBacklogReferenceRequest request, CancellationToken cancellationToken = default);
    Task<BacklogReferenceDto> UpdateReferenceAsync(string referenceType, string key, UpdateBacklogReferenceRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BacklogIntakeDto>> GetIntakeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BacklogAuditEventDto>> GetHistoryAsync(Guid storyId, CancellationToken cancellationToken = default);
    Task<string> ExportMarkdownAsync(CancellationToken cancellationToken = default);
}
