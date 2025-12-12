using System.Reflection;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Telemetry;

namespace CognitivePlatform.Api.Execution;

public class ExecutionEngine : IExecutionEngine
{
    private readonly ITelemetrySink _telemetry;

    public ExecutionEngine(ITelemetrySink telemetry)
    {
        _telemetry = telemetry;
    }

    public string Execute (ActionMetadata              action
                         , IDictionary<string, string> arguments)
    {
        _telemetry.Track("Execution.Start", action.Name);

        try
        {
            // Adjust this property name to match your ActionMetadata:
            // e.g., action.Method or action.MethodInfo
            var methodInfo = action.MethodInfo;

            if (methodInfo is null)
                return $"Failed to execute action '{action.Name}': MethodInfo was null.";

            var target = CreateTargetInstance(methodInfo);

            var parameters = methodInfo.GetParameters();
            var args = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var paramName = p.Name ?? $"arg{i}";

                if (!arguments.TryGetValue(paramName, out var stringValue))
                {
                    // If no argument supplied:
                    if (p.HasDefaultValue)
                    {
                        args[i] = p.DefaultValue;
                        continue;
                    }

                    if (p.ParameterType == typeof(string))
                    {
                        args[i] = null;
                        continue;
                    }

                    // You can choose to be stricter here if you want
                    throw new InvalidOperationException(
                        $"Missing required parameter '{paramName}'.");
                }

                args[i] = ConvertStringToType(stringValue, p.ParameterType);
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

            var formatted = FormatResult(result);

            _telemetry.Track("Execution.End", $"Execution succeeded. Output='{formatted}'");

            return formatted;
        }
        catch (Exception ex)
        {
            var message = $"Failed to execute action: {ex.Message}";
            _telemetry.Track("Execution.End", message);
            return message;
        }
    }

    private static object? ConvertStringToType(string value, Type targetType)
    {
        if (targetType == typeof(string))
            return value;

        if (targetType == typeof(int) || targetType == typeof(int?))
            return int.TryParse(value, out var i) ? i : default(int?);

        if (targetType == typeof(long) || targetType == typeof(long?))
            return long.TryParse(value, out var l) ? l : default(long?);

        if (targetType == typeof(bool) || targetType == typeof(bool?))
            return bool.TryParse(value, out var b) ? b : default(bool?);

        if (targetType.IsEnum)
        {
            try
            {
                return Enum.Parse(targetType, value, ignoreCase: true);
            }
            catch
            {
                return Activator.CreateInstance(targetType);
            }
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
        if (declaringType is null) return null;

        return Activator.CreateInstance(declaringType);
    }

    private static string FormatResult(object? result)
    {
        if (result is null) return "Action executed successfully (no return value).";

        return result.ToString()
               ?? "Action executed, but result was not representable as text.";
    }
}


// using System.Reflection;
// using CognitivePlatform.Api.Models;
// using CognitivePlatform.Api.Telemetry;
//
// namespace CognitivePlatform.Api.Execution;
//
// public class ExecutionEngine : IExecutionEngine
// {
//     private readonly ITelemetrySink _telemetry;
//     
//     public ExecutionEngine(ITelemetrySink telemetry)
//     {
//         _telemetry = telemetry;
//     }
//     
//     public string Execute(ActionMetadata action)
//     {
//         _telemetry.Track("Execution.Start", action.Name);
//
//         if (action.MethodInfo is null)
//         {
//             return "Cannot execute action: no method information available.";
//         }
//
//         try
//         {
//             var targetInstance = GetTargetInstance(action.MethodInfo);
//             var result         = action.MethodInfo.Invoke(targetInstance
//                                                         , Array.Empty<object>());
//             _telemetry.Track("Execution.End", "Executed successfully.");
//
//             return FormatResult(result);
//         }
//         catch (TargetInvocationException ex)
//         {
//             var errorMessage = ex.InnerException?.Message ?? ex.Message;
//             
//             _telemetry.Track("Execution.End", $"Execution failed: {errorMessage}");
//
//             return $"Action threw an exception: {errorMessage}";
//         }
//         catch (Exception ex)
//         {
//             _telemetry.Track("Execution.End", $"Execution failed: {ex.Message}");
//             
//             return $"Failed to execute action: {ex.Message}";
//         }
//     }
//
//     private static object? GetTargetInstance(MethodInfo methodInfo)
//     {
//         if (methodInfo.IsStatic) return null;
//
//         var declaringType = methodInfo.DeclaringType;
//
//         if (declaringType is null) return null;
//
//         return Activator.CreateInstance(declaringType);
//     }
//
//     private static string FormatResult(object? result)
//     {
//         if (result is null) return "Action executed successfully (no return value).";
//
//         return result.ToString()
//             ?? "Action executed, but result was not representable as text.";
//     }
// }