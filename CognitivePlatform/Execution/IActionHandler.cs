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

public sealed class ActionExecutionContext
{
    public ActionExecutionContext( string                       actionName
                                 , IDictionary<string, string>  parameters
                                 , IServiceProvider             serviceProvider
                                 , string                       sessionId
                                 , CancellationToken            cancellationToken )
    {
        ActionName        = actionName;
        Parameters        = parameters;
        ServiceProvider   = serviceProvider;
        SessionId         = sessionId;
        CancellationToken = cancellationToken;
    }

    public string                      ActionName        { get; }
    public IDictionary<string, string> Parameters        { get; }
    public IServiceProvider            ServiceProvider   { get; }
    public string                      SessionId         { get; }
    public CancellationToken           CancellationToken { get; }

    /// <summary>Convenience: resolve a required service from the container.</summary>
    public TService Require<TService>() where TService : notnull
        => ServiceProvider.GetRequiredService<TService>();
}
