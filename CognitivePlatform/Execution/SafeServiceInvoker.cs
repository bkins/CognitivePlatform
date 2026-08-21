using System.Reflection;
using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Api.Execution;

public sealed class SafeServiceInvoker : IServiceInvoker
{
    public async Task<ActionResult> InvokeAsync(MethodInfo method
                                              , object     service
                                              , object[]   args
                                              , bool       dryRun)
    {
        try
        {
            var result = method.Invoke(service, args);

            return await ActionResultUtility.UnwrapAsync(result)
                ?? new ActionResult { Message = "No result returned." };
        }
        catch (Exception ex)
            when (IsDatabaseConnectivityIssue(ex))
        {
            return HandleException("Database unavailable.", ex);
        }
        catch (Exception ex)
        {
            return HandleException("Unknown error. Try again later?", ex);
        }
    }

    private static ActionResult HandleException(string    message
                                              , Exception exception)
    {
        return new ActionResult
               {
                   Message = message
                 , Logs    = [exception.InnerException?.Message ?? exception.Message]
               };
    }

    private static bool IsDatabaseConnectivityIssue(Exception? ex)
    {
        // MethodInfo.Invoke wraps thrown exceptions in TargetInvocationException,
        // so check the inner exception for the real cause.
        var cause = ex?.InnerException ?? ex;

        var isTransportLevelError = cause?.Message.ContainsIgnoreCase("transport-level error")
                                 ?? false;

        return cause is SqliteException
            || cause is TimeoutException
            || isTransportLevelError;
    }
}
