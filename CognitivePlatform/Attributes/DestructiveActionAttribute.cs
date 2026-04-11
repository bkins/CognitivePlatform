namespace CognitivePlatform.Api.Attributes;

/// <summary>
/// Marks a natural language action as destructive.
///
/// The orchestrator will intercept any destructive action before execution
/// and require explicit user confirmation. If the user confirms, the action
/// executes normally. If the user cancels, execution is aborted.
///
/// Apply alongside [NaturalLanguageAction]. Actions decorated with this
/// attribute should be irreversible or have significant side-effects
/// (e.g. deleting data, sending messages, modifying shared state).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DestructiveActionAttribute : Attribute { }
