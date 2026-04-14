namespace CognitivePlatform.Admin.CpAdminClients;

/// <summary>
/// Targets /api/admin/knowledge — list (incl. deleted), hard-delete, restore, direct inject.
/// </summary>
public interface IAdminKnowledgeClient
{
    Task<IReadOnlyList<KnowledgeItemAdminDto>> GetAllAsync(CancellationToken ct = default);

    Task<bool> HardDeleteAsync(string id, CancellationToken ct = default);

    Task<bool> RestoreAsync(string id, CancellationToken ct = default);

    Task<string?> InjectAsync(InjectKnowledgeRequest request, CancellationToken ct = default);
}
