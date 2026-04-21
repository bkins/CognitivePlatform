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
}
