namespace CognitivePlatform.Admin.CpAdminClients;

/// <summary>
/// Targets /api/admin/journal — full entry browser with revision history
/// and admin correction revision authoring.
/// </summary>
public interface IAdminJournalClient
{
    Task<IReadOnlyList<JournalEntryAdminDto>> GetEntriesAsync( CancellationToken ct = default );

    Task<IReadOnlyList<JournalRevisionAdminDto>> GetRevisionsAsync( string            entryId
                                                                  , CancellationToken ct = default );

    Task<string?> AddCorrectionAsync( string                       entryId
                                    , AddCorrectionRevisionRequest request
                                    , CancellationToken            ct = default );

    Task<string?> CreateEntryAsync( CreateJournalEntryAdminDto request
                                  , CancellationToken          ct = default );

    Task<bool> SoftDeleteEntryAsync( string                       entryId
                                   , SoftDeleteJournalAdminDto?   request = null
                                   , CancellationToken            ct = default );

    Task<bool> RestoreEntryAsync( string            entryId
                                , CancellationToken ct = default );

    Task<bool> HardDeleteEntryAsync( string            entryId
                                   , CancellationToken ct = default );

    Task<bool> UpdateRevisionAsync( string                         entryId
                                  , string                         revisionId
                                  , UpdateJournalRevisionAdminDto  request
                                  , CancellationToken              ct = default );

    Task<RepairPartitionKeysResultDto?> RepairPartitionKeysAsync(CancellationToken ct = default);
}
