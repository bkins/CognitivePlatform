namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Controlled bridge from insight suggestion to autonomous action execution.
/// Every call is audit-logged. The default implementation allows no actions
/// unless the action name appears in the injected whitelist.
/// </summary>
public interface IAutomationGate
{
    bool CanAutoExecute(string actionName, IDictionary<string, string> parameters);
}
