namespace CognitivePlatform.Api.Interpreter;

public sealed record ExecutionProfile(
    ExecutionProfileKind Kind,
    TaskComplexity       PreferredComplexity,
    bool                 RequiresGrounding,
    bool                 EnforceStrictConfirmation);
