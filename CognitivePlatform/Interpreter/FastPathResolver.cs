using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Avails.Extensions;

namespace CognitivePlatform.Api.Interpreter;

public sealed class FastPathResolver : IFastPathResolver
{
    private readonly IActionRegistry _registry;

    public FastPathResolver(IActionRegistry registry)
    {
        _registry = registry;
    }

    public bool TryResolve(string                           input
                          , out ActionMetadata?             action
                          , out Dictionary<string, string>? parameters)
    {
        action     = null;
        parameters = null;

        input = input.Trim();

        // ------------------------------------------------------------
        // MODE 0: SYSTEM CAPABILITIES
        // ------------------------------------------------------------
        if (IsCapabilitiesQuery(input))
        {
            action = _registry.Actions
                              .FirstOrDefault(action => action.Name == "ListActions");

            parameters = new Dictionary<string, string>();
            
            return action != null;
        }
        
        // ------------------------------------------------------------
        // MODE 1.1: EXPLICIT "<actionName>:" PREFIX (e.g. "Journal: ...")
        // ------------------------------------------------------------
        if (TryResolveColonPrefix(input, out action, out parameters))
        {
            return true;
        }

        // ------------------------------------------------------------
        // MODE 1.2: PREFIX COMMANDS
        // ------------------------------------------------------------
        if (input.StartsWith("/"))
            return TryResolvePrefix(input, out action, out parameters);

        // ------------------------------------------------------------
        // MODE 2: GENERAL FAST PATH (ATTRIBUTE + METADATA DRIVEN)
        // ------------------------------------------------------------
        if (TryResolveGenericFastPath(input, out action, out parameters))
            return true;

        return false;
    }
    private bool TryResolveColonPrefix(string                           input
                                      , out ActionMetadata?             action
                                      , out Dictionary<string, string>? parameters)
    {
        action     = null;
        parameters = null;

        var colonIndex = input.IndexOf(':');
        if (colonIndex <= 0) return false;

        var prefix  = input[..colonIndex].Trim();       // e.g. "Journal"
        if (string.IsNullOrWhiteSpace(prefix)) return false;

        if (prefix.Equals("journal", StringComparison.OrdinalIgnoreCase))
        {
            action = _registry.Actions.FirstOrDefault(action => action.Name == "AddJournalEntry");
            if (action is null) return false;

            input = input.Replace("journal:"
                                , "text:"
                                , StringComparison.OrdinalIgnoreCase);
            
            parameters = ParseToDictionary(input);
            
            return true;
        }

        // // Optional: allow exact action name prefix: "AddJournalEntry: ..."
        // var direct = _registry.Actions.FirstOrDefault(action => action.Name.Equals(prefix, StringComparison.OrdinalIgnoreCase));
        // if (direct is null || direct.Parameters.Count <= 0) return false;
        //
        // // Only safe if exactly one required param
        // var required = direct.Parameters.Where(p => p.IsOptional.Not()).ToList();
        //
        // if (required.Count != 1) return false;
        //
        // parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        //              {
        //                      [required[0].Name] = payload
        //              };
        //
        // action = direct;
        //
        // return true;
        return false;
    }

    public static Dictionary<string, string> ParseToDictionary(string input)
    {
        return input.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(line => line.Split(':', 2, StringSplitOptions.TrimEntries))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
    }
    
    // ================================================================
    // PREFIX COMMAND MODE (/journal add "text")
    // ================================================================
    private bool TryResolvePrefix(string                           input
                                 , out ActionMetadata?             action
                                 , out Dictionary<string, string>? parameters)
    {
        action = null;
        parameters = null;

        var parts = input.Substring(1).Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
            return false;

        var domain = parts[0].ToLowerInvariant();

        // /journal ...
        if (domain == "journal")
        {
            if (parts.Length >= 2)
            {
                var verb = parts[1].ToLowerInvariant();

                if (verb == "add" && parts.Length == 3)
                {
                    action = _registry.Actions.FirstOrDefault(a => a.Name == "AddJournalEntry");
                    if (action == null) return false;

                    parameters = new Dictionary<string, string>
                    {
                        ["text"] = parts[2]
                    };
                    return true;
                }

                if (verb == "history")
                {
                    action = _registry.Actions.FirstOrDefault(a => a.Name == "JournalEntriesOnThisDay");
                    parameters = new();
                    return action != null;
                }

                if (verb == "list")
                {
                    action = _registry.Actions.FirstOrDefault(a => a.Name == "ListJournalEntries");
                    parameters = new();
                    return action != null;
                }
            }
        }

        return false;
    }

    // ================================================================
    // MODE 2: GENERIC FAST PATH — USING METADATA AND ATTRIBUTES
    // ================================================================
    private bool TryResolveGenericFastPath(string                           input
                                          , out ActionMetadata?             action
                                          , out Dictionary<string, string>? parameters)
    {
        action = null;
        parameters = null;

        var normalized = input.ToLowerInvariant();

        // 1) Find actions marked [FastPath]
        var candidates = _registry.FastPathActions;

        if (candidates.Any().Not())
            return false;

        // 2) For each candidate, find required parameters
        foreach (var meta in candidates)
        {
            var requiredParams = meta.Parameters
                                     .Where(parameter => parameter.IsOptional.Not())
                                     .ToList();

            // Fast-path rules:
            // Only consider actions with exactly ONE required parameter.
            if (requiredParams.Count != 1)
                continue;

            var mainParam = requiredParams.Single();

            // 3) Whitelist fast-path language triggers
            if (ContainsFastPathSignal(normalized).Not()) continue;

            // Extract text after the trigger
            var extracted = ExtractPrimaryValue(normalized);
            if (string.IsNullOrWhiteSpace(extracted))
                continue;

            action = meta;
            parameters = new Dictionary<string, string>
            {
                [mainParam.Name] = extracted
            };
            return true;
        }

        return false;
    }

    // ================================================================
    // SIGNAL DETECTION — NATURAL LANGUAGE INTENT MARKERS
    // ================================================================
    private static readonly string[] FastPathSignals =
    {
            "note that "
          , "remember to "
          , "remember this "
          , "quick note "
          , "write down "
          , "jot down "
          , "task to "
          , "i need to "
          , "log that "
          , "log this "
          , "add task "
          , "create task "
          , "journal this "
          , "journal that "
          , "journal"
    };

    private static bool ContainsFastPathSignal(string input)
    {
        return FastPathSignals.Any(sig => input.Contains(sig));
    }

    // ================================================================
    // PRIMARY VALUE EXTRACTION
    // ================================================================
    private static string ExtractPrimaryValue(string input)
    {
        foreach (var sig in FastPathSignals)
        {
            if (input.DoesNotContainOrNull(sig)) continue;
            
            var pos = input.IndexOf(sig, StringComparison.Ordinal);
            if (pos >= 0)
            {
                return input.Substring(pos + sig.Length).Trim();
            }
        }

        return string.Empty;
    }
    
    private static bool IsCapabilitiesQuery(string input)
    {
        var normalized = input.Trim()
                              .TrimEnd('?', '!', '.')
                              .ToLowerInvariant();

        return normalized is
                       "what can you do"
                    or "what can i do"
                    or "help"
                    or "list actions"
                    or "list commands"
                    or "show commands"
                    or "show capabilities";
    }

}



// Version 1

// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Text.RegularExpressions;
// using CognitivePlatform.Api.Models;
// using CognitivePlatform.Api.Registry;
//
// namespace CognitivePlatform.Api.Interpreter;
//
// public sealed class FastPathResolver
// {
//     private readonly IActionRegistry _registry;
//
//     public FastPathResolver(IActionRegistry registry)
//     {
//         _registry = registry;
//     }
//
//     public bool TryResolve(string                           input
//                           , out ActionMetadata?             action
//                           , out Dictionary<string, string>? parameters)
//     {
//         action = null;
//         parameters = null;
//
//         input = input.Trim();
//
//         // ------------------------------------------------------------
//         // MODE 1: PREFIX COMMANDS (start with "/")
//         // ------------------------------------------------------------
//         if (input.StartsWith("/"))
//         {
//             return TryResolvePrefix(input, out action, out parameters);
//         }
//
//         // ------------------------------------------------------------
//         // MODE 2: DETERMINISTIC NLP RULES
//         // ------------------------------------------------------------
//         if (TryResolveDeterministic(input, out action, out parameters))
//         {
//             return true;
//         }
//
//         return false;
//     }
//
//     // TODO: 
//     // 1. At least, move literal strings to consts
//     // 2. Explore ways of generalizing, and/or getting the string values from Attribute(s)
//     
//     // ================================================================
//     // PREFIX COMMAND MODE (/journal add "text")
//     // ================================================================
//     private bool TryResolvePrefix(string                           input
//                                  , out ActionMetadata?             action
//                                  , out Dictionary<string, string>? parameters)
//     {
//         action = null;
//         parameters = null;
//
//         // Remove leading slash
//         var parts = input.Substring(1).Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
//
//         if (parts.Length == 0)
//             return false;
//
//         var domain = parts[0].ToLowerInvariant();
//
//         // /journal ...
//         if (domain == "journal")
//         {
//             if (parts.Length >= 2)
//             {
//                 var verb = parts[1].ToLowerInvariant();
//
//                 // /journal add <text>
//                 if (verb == "add" 
//                  && parts.Length == 3)
//                 {
//                     var meta = _registry.Actions
//                                         .FirstOrDefault(action => action.Name == "AddJournalEntry");
//
//                     if (meta is null) return false;
//
//                     action = meta;
//                     parameters = new Dictionary<string, string>
//                     {
//                         ["text"] = parts[2]
//                     };
//                     return true;
//                 }
//
//                 // /journal history
//                 if (verb == "history")
//                 {
//                     action = _registry.Actions
//                                       .FirstOrDefault(action => action.Name == "JournalEntriesOnThisDay");
//
//                     parameters = new Dictionary<string, string>();
//                     return action != null;
//                 }
//
//                 // /journal list
//                 if (verb == "list")
//                 {
//                     action = _registry.Actions
//                                       .FirstOrDefault(action => action.Name == "ListJournalEntries");
//
//                     parameters = new Dictionary<string, string>();
//                     return action != null;
//                 }
//             }
//         }
//
//         return false;
//     }
//
//     // ================================================================
//     // NATURAL LANGUAGE FAST RULES
//     // ================================================================
//     private bool TryResolveDeterministic(string                           input
//                                         , out ActionMetadata?             action
//                                         , out Dictionary<string, string>? parameters)
//     {
//         action = null;
//         parameters = null;
//
//         var normalized = input.ToLowerInvariant();
//
//         // Add journal entry
//         if (normalized.StartsWith("add journal entry") 
//           || normalized.StartsWith("write in my journal") 
//           || normalized.StartsWith("journal add"))
//         {
//             var meta = _registry.Actions
//                                 .FirstOrDefault(action => action.Name == "AddJournalEntry");
//
//             if (meta is null) return false;
//
//             // Try to extract text by removing known prefixes
//             var text = normalized.Replace("add journal entry", "")
//                                  .Replace("write in my journal", "")
//                                  .Replace("journal add", "")
//                                  .Trim();
//
//             if (string.IsNullOrWhiteSpace(text))
//                 return false;
//
//             action = meta;
//             parameters = new Dictionary<string, string>
//             {
//                 ["text"] = text
//             };
//             return true;
//         }
//
//         // This day in history
//         if (normalized.Contains("this day in history") 
//           || normalized.Contains("this day over the years") 
//           || normalized.Contains("today but past years"))
//         {
//             action = _registry.Actions
//                               .FirstOrDefault(a => a.Name == "JournalEntriesOnThisDay");
//
//             parameters = new Dictionary<string, string>();
//             return action != null;
//         }
//
//         return false;
//     }
// }
