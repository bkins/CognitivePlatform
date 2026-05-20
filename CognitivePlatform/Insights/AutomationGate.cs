using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Phase E automation bridge. Allows autonomous action execution only for
/// actions explicitly whitelisted at registration time. All calls are
/// audit-logged regardless of outcome.
///
/// <para>
/// Design invariants:
/// <list type="bullet">
///   <item>No blanket enable — the default whitelist is empty.</item>
///   <item>Every call produces an Information log entry (action, result, timestamp).</item>
///   <item>All automation still flows through the Safety layer and Orchestrator;
///         this gate is the first checkpoint, not a bypass.</item>
/// </list>
/// </para>
/// </summary>
public sealed class AutomationGate : IAutomationGate
{
    private readonly IReadOnlySet<string>    _allowedActions;
    private readonly ILogger<AutomationGate> _logger;

    public AutomationGate( IReadOnlySet<string>    allowedActions
                          , ILogger<AutomationGate> logger )
    {
        _allowedActions = allowedActions ?? throw new ArgumentNullException(nameof(allowedActions));
        _logger         = logger         ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanAutoExecute(string actionName, IDictionary<string, string> parameters)
    {
        var allowed = _allowedActions.Contains(actionName);

        _logger.LogInformation(
            "AutomationGate.CanAutoExecute: Action={ActionName} Allowed={Allowed} Timestamp={Timestamp:O}"
          , actionName
          , allowed
          , DateTimeOffset.UtcNow);

        return allowed;
    }
}
