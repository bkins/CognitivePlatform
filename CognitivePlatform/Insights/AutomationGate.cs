using System.Text.Json;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Workspace;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Phase E automation bridge (updated for dynamic user settings & persistent audit logging).
/// Allows autonomous action execution only for actions explicitly whitelisted in the user settings.
/// Every check is persisted to the Object Store as an audit log.
/// </summary>
public sealed class AutomationGate : IAutomationGate
{
    private readonly IUserSettingsService    _settingsService;
    private readonly IObjectStore             _store;
    private readonly ILogger<AutomationGate> _logger;

    private const string AuditPartitionKey = "automation-audit";

    public AutomationGate( IUserSettingsService    settingsService
                          , IObjectStore             store
                          , ILogger<AutomationGate> logger )
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _store           = store           ?? throw new ArgumentNullException(nameof(store));
        _logger          = logger          ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanAutoExecute(string actionName, IDictionary<string, string> parameters)
    {
        var settings = _settingsService.Get();
        var allowed = settings.AllowedAutomationActions.Contains(actionName);

        _logger.LogInformation(
            "AutomationGate.CanAutoExecute: Action={ActionName} Allowed={Allowed} Timestamp={Timestamp:O}"
          , actionName
          , allowed
          , DateTimeOffset.UtcNow);

        // Run fire-and-forget or synchronous save for the audit log.
        // Since this is in-turn, we can run synchronously or via Task.Run, but saving is fast.
        var audit = new AutomationAudit
        {
            ActionName = actionName,
            Allowed = allowed,
            ParametersJson = JsonSerializer.Serialize(parameters),
            CheckedAtUtc = DateTime.UtcNow
        };

        // We run synchronously on the calling thread or task wait to ensure durability.
        try
        {
            _store.Save(audit, partitionKey: AuditPartitionKey, id: audit.Id).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save AutomationAudit log to Object Store.");
        }

        return allowed;
    }
}
