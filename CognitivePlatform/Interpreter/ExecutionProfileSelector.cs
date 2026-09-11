using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Interpreter;

public sealed class ExecutionProfileSelector : IExecutionProfileSelector
{
    private static readonly string[] SafetyKeywords =
    {
        "delete"
      , "remove"
      , "deploy"
      , "release"
      , "production"
      , "secret"
      , "permission"
      , "configuration"
    };

    private static readonly string[] CodingKeywords =
    {
        "code"
      , "bug"
      , "exception"
      , "compile"
      , "test"
      , "refactor"
      , "repository"
      , "api"
    };

    private static readonly string[] ResearchKeywords =
    {
        "research"
      , "source"
      , "compare"
      , "analyze"
      , "synthesize"
      , "investigate"
      , "deep dive"
    };

    public ExecutionProfile SelectProfile(string userMessage)
    {
        var message = userMessage ?? string.Empty;

        if (ContainsAny(message, SafetyKeywords)) return new ExecutionProfile(ExecutionProfileKind.SafetyCritical, TaskComplexity.Standard, false, true);
        if (ContainsAny(message, CodingKeywords)) return new ExecutionProfile(ExecutionProfileKind.Coding, TaskComplexity.Heavy, false, false);
        if (ContainsAny(message, ResearchKeywords)) return new ExecutionProfile(ExecutionProfileKind.Research, TaskComplexity.Heavy, true, false);
        return new ExecutionProfile(ExecutionProfileKind.Chat, TaskComplexity.Light, false, false);
    }

    private static bool ContainsAny(string message, IReadOnlyList<string> keywords)
    {
        return keywords.Any(keyword => message.ContainsIgnoreCase(keyword));
    }
}
