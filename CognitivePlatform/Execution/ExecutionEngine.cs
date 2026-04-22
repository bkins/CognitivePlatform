using System.Reflection;
using CognitivePlatform.Api.Audit;
using CognitivePlatform.Api.Avails.Extensions;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Telemetry;
using CognitivePlatform.Api.Telemetry.Events;

namespace CognitivePlatform.Api.Execution;

public class ExecutionEngine : IExecutionEngine
{
    private readonly ITelemetrySink   _telemetry;
    private readonly TelemetryContext _telemetryContext;
    private readonly IAuditLog        _auditLog;
    // Static provider is a bootstrap convenience — scoped action instances are resolved here.
    // Flagged as suspect (DEFERRED: introduce proper scoped DI for action classes).
    private static   IServiceProvider _serviceProvider;

    public ExecutionEngine( ITelemetrySink   telemetry
                          , IServiceProvider serviceProvider
                          , TelemetryContext telemetryContext
                          , IAuditLog        auditLog )
    {
        _telemetry        = telemetry;
        _serviceProvider  = serviceProvider;
        _telemetryContext = telemetryContext;
        _auditLog         = auditLog;
    }

    public async Task<string> ExecuteAsync( ActionMetadata              action
                                          , IDictionary<string, string> arguments
                                          , string                      sessionId
                                          , CancellationToken           ct = default )
    {
        _telemetry.Track(_telemetryContext.CreateEvent(new ExecutionStartedEvent
                                                       {
                                                               ActionName = action.Name
                                                       }));

        var paramSummary = string.Join(", ", arguments.Select(p => $"{p.Key}={p.Value}"));

        try
        {
            var methodInfo = action.MethodInfo;

            if (methodInfo is null)
                return $"Failed to execute action '{action.Name}': MethodInfo was null.";

            var target = CreateTargetInstance(methodInfo);

            var parameters = methodInfo.GetParameters();
            var args       = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var paramName = parameter.Name ?? $"arg{i}";

                if (arguments.TryGetValue(paramName, out var stringValue).Not())
                {
                    if (parameter.HasDefaultValue)
                    {
                        args[i] = parameter.DefaultValue;
                        continue;
                    }

                    if (parameter.ParameterType != typeof(string))
                        throw new InvalidOperationException($"Missing required parameter '{paramName}'.");

                    args[i] = null;
                    continue;
                }

                args[i] = ConvertStringToType(stringValue, parameter.ParameterType);
            }

            object? rawResult;

            try
            {
                rawResult = methodInfo.Invoke(target, args);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                throw tie.InnerException;
            }

            // Await async action results. Reflected Invoke always returns the Task object
            // itself (not the awaited value), so we await it here and then read .Result.
            var result = rawResult;
            if (rawResult is Task task)
            {
                await task.ConfigureAwait(false);

                var taskType = task.GetType();

                if (taskType.IsGenericType)
                {
                    // VoidTaskResult is .NET's internal void-equivalent; treat as no value.
                    var typeArg = taskType.GetGenericArguments()[0];
                    result = typeArg.Name == "VoidTaskResult"
                                     ? null
                                     : taskType.GetProperty("Result")?.GetValue(task);
                }
                else
                {
                    result = null;
                }
            }

            var formatted = FormatResult(result);

            _telemetry.Track(_telemetryContext.CreateEvent(new ExecutionCompletedEvent
                                                           {
                                                                   ActionName = action.Name
                                                                 , Success    = true
                                                                 , Output     = formatted
                                                           }));

            await _auditLog.AppendAsync(new AuditEvent
                                        {
                                                ActionName  = action.Name
                                              , Parameters  = paramSummary
                                              , Outcome     = AuditOutcome.Success
                                              , Meta        = { ["sessionId"] = sessionId }
                                        }).ConfigureAwait(false);

            return formatted;
        }
        catch (Exception ex)
        {
            var message = $"Failed to execute action: {ex.Message}";

            _telemetry.Track(_telemetryContext.CreateEvent(new ExecutionCompletedEvent
                                                           {
                                                                   ActionName = action.Name
                                                                 , Success    = false
                                                                 , Error      = ex.Message
                                                           }));

            await _auditLog.AppendAsync(new AuditEvent
                                        {
                                                ActionName   = action.Name
                                              , Parameters   = paramSummary
                                              , Outcome      = AuditOutcome.Failure
                                              , ErrorMessage = ex.Message
                                              , Meta         = { ["sessionId"] = sessionId }
                                        }).ConfigureAwait(false);

            return message;
        }
    }

    // -----------------------------------------------------------------------
    // Type conversion
    // -----------------------------------------------------------------------

    private static object? ConvertStringToType( string value
                                              , Type   targetType )
    {
        if (targetType == typeof(string))
            return value;

        if (targetType == typeof(int)
         || targetType == typeof(int?))
            return int.TryParse(value, out var i) ? i : default(int?);

        if (targetType == typeof(long)
         || targetType == typeof(long?))
            return long.TryParse(value, out var l) ? l : default(long?);

        if (targetType == typeof(bool)
         || targetType == typeof(bool?))
            return bool.TryParse(value, out var b) ? b : default(bool?);

        // Handle Nullable<TEnum> — e.g. TaskPriority?
        var underlyingType = Nullable.GetUnderlyingType(targetType);
        if (underlyingType is not null && underlyingType.IsEnum)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            try   { return Enum.Parse(underlyingType, value, ignoreCase: true); }
            catch { return null; }
        }

        // Handle non-nullable enums
        if (targetType.IsEnum)
        {
            try   { return Enum.Parse(targetType, value, ignoreCase: true); }
            catch { return Activator.CreateInstance(targetType); }
        }

        try
        {
            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }
    }

    private static object? CreateTargetInstance(MethodInfo methodInfo)
    {
        if (methodInfo.IsStatic) return null;

        var declaringType = methodInfo.DeclaringType;

        return declaringType is null
                       ? null
                       : _serviceProvider.GetRequiredService(declaringType);
    }

    private static string FormatResult(object? result)
    {
        if (result is null)
            return "Action executed successfully (no return value).";

        return result.ToString()
            ?? "Action executed, but result was not representable as text.";
    }
}
