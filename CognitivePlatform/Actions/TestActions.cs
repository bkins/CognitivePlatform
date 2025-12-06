using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Conversation;

namespace CognitivePlatform.Api.Actions;

public static class TestActions
{
    private static ConversationContext? _context;

    // Called by orchestrator once per request/session
    public static void SetContext(ConversationContext context)
    {
        _context = context;
    }

    // ---------------------------------------------------------------------
    // 1. StoreValue
    // ---------------------------------------------------------------------
    [NaturalLanguageAction(Description = "Stores a value in the session context."
                         , Examples = new[]
                                      {
                                          "Remember that my favorite number is 42"
                                        , "Set x to 100"
                                        , "Store the value blue"
                                      }
                          , Category = "memory")]
    public static string StoreValue ([NaturalLanguageParam(Description = "The name of the variable to store.")] 
                                     string key
                                   , [NaturalLanguageParam(Description = "The value to store under the key.")]  
                                     string value)
    {
        if (_context is null) return "No conversation context available.";

        _context.Metadata[key] = value;

        return $"Stored '{value}' under key '{key}'.";
    }

    // ---------------------------------------------------------------------
    // 2. RecallValue
    // ---------------------------------------------------------------------
    [NaturalLanguageAction(Description = "Retrieves a previously stored value."
                         , Examples    = new[]
                                         {
                                             "What is x?"
                                           , "What did I say earlier?"
                                           , "Recall the value of count"
                                         }
                         , Category = "memory")]
    public static string RecallValue([NaturalLanguageParam(Description = "The variable name to retrieve.")]
                                     string key)
    {
        if (_context is null)
            return "No conversation context available.";

        if (_context.Metadata.TryGetValue(key, out var value))
            return $"The stored value of '{key}' is '{value}'.";

        return $"No stored value found for '{key}'.";
    }

    // ---------------------------------------------------------------------
    // 3. RepeatLastAction
    // ---------------------------------------------------------------------
    [NaturalLanguageAction( Description = "Repeats the last action using the previously extracted parameters."
                          , Examples    = new[]
                                          {
                                              "Do that again"
                                            , "Repeat the last command"
                                            , "Run it again"
                                          }
                          , Category = "meta")]
    public static string RepeatLastAction()
    {
        if (_context is null)
            return "No conversation context available.";

        if (_context.LastActionName is null)
            return "There is no previous action to repeat.";

        var summary = _context.LastParameters.Count == 0
            ? "(no stored parameters)"
            : string.Join(", ", _context.LastParameters.Select(p => $"{p.Key}={p.Value}"));

        return $"Request to repeat action '{_context.LastActionName}' with parameters: {summary}";
    }
}
