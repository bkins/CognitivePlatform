namespace CognitivePlatform.Tests;

/// <summary>
/// Groups test classes that share the LlmActions static context fields
/// (_context, _catalog, _providerDefaults). xUnit runs tests within the
/// same collection sequentially, preventing the race condition where an
/// orchestrator test sets an empty catalog just before a LlmActionsTests
/// assertion reads it.
/// </summary>
[CollectionDefinition("LlmSharedState")]
public class LlmSharedStateCollection { }
