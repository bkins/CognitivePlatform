namespace CognitivePlatform.Admin.CpAdminClients;

/// <summary>
/// Targets GET /api/admin/registry — action registry browser.
/// Also provides test-invoke via POST /api/conversation/converse.
/// </summary>
public interface IAdminRegistryClient
{
    Task<IReadOnlyList<ActionMetadataDto>> GetActionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Sends <paramref name="input"/> to the normal conversation endpoint
    /// and returns the result. Used by the Registry Browser test-invoke panel.
    /// </summary>
    Task<ConverseResultDto?> TestInvokeAsync(string input, CancellationToken ct = default);
}
