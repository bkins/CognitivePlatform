using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components;
using CognitivePlatform.Admin.Services;

namespace CognitivePlatform.Admin.Components;

/// <summary>
/// Wraps ErrorBoundary to log render exceptions to AdminErrorLog.
/// OnErrorAsync was removed as a parameter in .NET 10; subclassing is the replacement pattern.
/// </summary>
public sealed class LoggingErrorBoundary : ErrorBoundary
{
    [Inject] private AdminErrorLog ErrorLog { get; set; } = default!;

    protected override Task OnErrorAsync(Exception exception)
    {
        ErrorLog.Add( level    : "ERR"
                    , category : $"Admin.Component.{exception.GetType().Name}"
                    , message  : exception.Message
                    , exception: exception );
        return Task.CompletedTask;
    }
}
