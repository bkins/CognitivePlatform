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

    public string Execute( ActionMetadata              action
                         , IDictionary<string, string> arguments
                         , string                      sessionId )
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

            object? result;

            try
            {
                result = methodInfo.Invoke(target, args);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                throw tie.InnerException;
            }

            // Unwrap Task / Task<T> so async actions work through the standard
            // execution path. When ExecutionEngine is made fully async (see
            // DEFERRED.md), replace this with a proper await.
            result = UnwrapTaskResult(result);

            var formatted = FormatResult(result);

            _telemetry.Track(_telemetryContext.CreateEvent(new ExecutionCompletedEvent
                                                           {
                                                                   ActionName = action.Name
                                                                 , Success    = true
                                                                 , Output     = formatted
                                                           }));

            _auditLog.Append(new AuditEvent
                             {
                                     ActionName  = action.Name
                                   , Parameters  = paramSummary
                                   , Outcome     = AuditOutcome.Success
                                   , Meta        = { ["sessionId"] = sessionId }
                             });

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

            _auditLog.Append(new AuditEvent
                             {
                                     ActionName   = action.Name
                                   , Parameters   = paramSummary
                                   , Outcome      = AuditOutcome.Failure
                                   , ErrorMessage = ex.Message
                                   , Meta         = { ["sessionId"] = sessionId }
                             });

            return message;
        }
    }

    // -----------------------------------------------------------------------
    // Async unwrapping
    // -----------------------------------------------------------------------

    /// <summary>
    /// If <paramref name="result"/> is a <see cref="Task"/> or <see cref="Task{T}"/>,
    /// waits for it to complete and returns the inner value (or null for non-generic Task).
    /// Non-task results pass through unchanged.
    ///
    /// Uses GetAwaiter().GetResult() because Execute is currently synchronous.
    /// When Execute is made async, replace with a proper await (see DEFERRED.md).
    /// </summary>
    private static object? UnwrapTaskResult(object? result)
    {
        if (result is not Task task)
            return result;

        task.GetAwaiter().GetResult();

        var taskType = task.GetType();

        if (!taskType.IsGenericType)
            return null;

        // VoidTaskResult is .NET's internal void-equivalent used by Task.CompletedTask
        // and async Task (non-generic) methods. Treat as no meaningful return value.
        var typeArg = taskType.GetGenericArguments()[0];
        if (typeArg.Name == "VoidTaskResult")
            return null;

        var resultProperty = taskType.GetProperty("Result");
        return resultProperty?.GetValue(task);
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
