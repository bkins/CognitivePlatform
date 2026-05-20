using CognitivePlatform.Api.Interpreter;

namespace CognitivePlatform.Api.Avails;

public static class KeyedServices
{
    // TODO: Implement these future `Interpreter`s
    /*
     * Consider:
        “llama3”
        “gpt-4.1-mini”
        “anthropic”
        “offline-mode”
        “deterministic-rulebased”
        etc...
     */
    public const string MockInterpreter = "mock";
    public const string LlmInterpreter  = "llm";

    public const string RuleBasedIntentAnalyzer = "intent-analyzer-rule";
    public const string KeywordIntentAnalyzer   = "intent-analyzer-keyword";
    public const string LlmIntentAnalyzer       = "intent-analyzer-llm";
}