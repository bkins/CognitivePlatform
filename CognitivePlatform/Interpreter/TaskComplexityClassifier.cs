namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Rule-based <see cref="ITaskComplexityClassifier"/>. Two ordered gates before
/// defaulting to <see cref="TaskComplexity.Standard"/>:
///   1. Heavy gate: a known reasoning/synthesis keyword anywhere in the input.
///   2. Light gate: short input or a known quick-lookup prefix.
/// </summary>
public sealed class TaskComplexityClassifier : ITaskComplexityClassifier
{
    private static readonly string[] HeavyKeywords =
    {
        "analyze"
      , "analysis"
      , "pattern"
      , "reflect"
      , "reflection"
      , "synthesize"
      , "comprehensive"
      , "in depth"
      , "deep dive"
      , "compare"
      , "personality snapshot"
      , "generate insight"
    };

    private static readonly string[] LightPrefixes =
    {
        "list"
      , "show"
      , "health"
      , "version"
    };

    private const int LightLengthThreshold = 25;

    public TaskComplexity Classify(string userMessage)
    {
        if (userMessage.HasNoValue())
            return TaskComplexity.Standard;

        var trimmed = userMessage.Trim();

        foreach (var keyword in HeavyKeywords)
        {
            if (trimmed.ContainsIgnoreCase(keyword))
                return TaskComplexity.Heavy;
        }

        if (trimmed.Length <= LightLengthThreshold)
            return TaskComplexity.Light;

        foreach (var prefix in LightPrefixes)
        {
            if (trimmed.StartsWithIgnoreCase(prefix))
                return TaskComplexity.Light;
        }

        return TaskComplexity.Standard;
    }
}
