namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Decides the <see cref="TaskComplexity"/> tier for a given user message.
/// Used by the orchestrator to advise the router on a model-tier preference
/// before the interpreter call is made.
/// </summary>
public interface ITaskComplexityClassifier
{
    TaskComplexity Classify(string userMessage);
}
