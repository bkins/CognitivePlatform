using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;

namespace CognitivePlatform.Api.Interpreter;

public sealed class FastPathResolver
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
        action = null;
        parameters = null;

        input = input.Trim();

        // ------------------------------------------------------------
        // MODE 1: PREFIX COMMANDS (start with "/")
        // ------------------------------------------------------------
        if (input.StartsWith("/"))
        {
            return TryResolvePrefix(input, out action, out parameters);
        }

        // ------------------------------------------------------------
        // MODE 2: DETERMINISTIC NLP RULES
        // ------------------------------------------------------------
        if (TryResolveDeterministic(input, out action, out parameters))
        {
            return true;
        }

        return false;
    }

    // TODO: 
    // 1. At least, move literal strings to consts
    // 2. Explore ways of generalizing, and/or getting the string values from Attribute(s)
    
    // ================================================================
    // PREFIX COMMAND MODE (/journal add "text")
    // ================================================================
    private bool TryResolvePrefix(string                           input
                                 , out ActionMetadata?             action
                                 , out Dictionary<string, string>? parameters)
    {
        action = null;
        parameters = null;

        // Remove leading slash
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

                // /journal add <text>
                if (verb == "add" 
                 && parts.Length == 3)
                {
                    var meta = _registry.Actions
                                        .FirstOrDefault(action => action.Name == "AddJournalEntry");

                    if (meta is null) return false;

                    action = meta;
                    parameters = new Dictionary<string, string>
                    {
                        ["text"] = parts[2]
                    };
                    return true;
                }

                // /journal history
                if (verb == "history")
                {
                    action = _registry.Actions
                                      .FirstOrDefault(action => action.Name == "JournalEntriesOnThisDay");

                    parameters = new Dictionary<string, string>();
                    return action != null;
                }

                // /journal list
                if (verb == "list")
                {
                    action = _registry.Actions
                                      .FirstOrDefault(action => action.Name == "ListJournalEntries");

                    parameters = new Dictionary<string, string>();
                    return action != null;
                }
            }
        }

        return false;
    }

    // ================================================================
    // NATURAL LANGUAGE FAST RULES
    // ================================================================
    private bool TryResolveDeterministic(string                           input
                                        , out ActionMetadata?             action
                                        , out Dictionary<string, string>? parameters)
    {
        action = null;
        parameters = null;

        var normalized = input.ToLowerInvariant();

        // Add journal entry
        if (normalized.StartsWith("add journal entry") 
          || normalized.StartsWith("write in my journal") 
          || normalized.StartsWith("journal add"))
        {
            var meta = _registry.Actions
                                .FirstOrDefault(action => action.Name == "AddJournalEntry");

            if (meta is null) return false;

            // Try to extract text by removing known prefixes
            var text = normalized.Replace("add journal entry", "")
                                 .Replace("write in my journal", "")
                                 .Replace("journal add", "")
                                 .Trim();

            if (string.IsNullOrWhiteSpace(text))
                return false;

            action = meta;
            parameters = new Dictionary<string, string>
            {
                ["text"] = text
            };
            return true;
        }

        // This day in history
        if (normalized.Contains("this day in history") 
          || normalized.Contains("this day over the years") 
          || normalized.Contains("today but past years"))
        {
            action = _registry.Actions
                              .FirstOrDefault(a => a.Name == "JournalEntriesOnThisDay");

            parameters = new Dictionary<string, string>();
            return action != null;
        }

        return false;
    }
}
