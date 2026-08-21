using Microsoft.Extensions.DependencyInjection;

namespace CognitivePlatform.Api.Execution;

/// <summary>
/// An explicit execution handler for an action that does not rely on
/// MethodInfo reflection. Used by programmatic and generated actions.
/// </summary>
public interface IActionHandler
{
    Task<ActionResult> ExecuteAsync(ActionExecutionContext context);
}

