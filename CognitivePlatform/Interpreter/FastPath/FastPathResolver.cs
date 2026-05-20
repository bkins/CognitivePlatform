using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Avails.Extensions;

namespace CognitivePlatform.Api.Interpreter;

public sealed class FastPathResolver : IFastPathResolver
{
    private readonly IActionRegistry           _registry;
    private readonly IDailyRecordCommandParser _dailyRecordParser;

    public FastPathResolver( IActionRegistry           registry
                           , IDailyRecordCommandParser dailyRecordParser)
    {
        _registry          = registry;
        _dailyRecordParser = dailyRecordParser;
    }

    public bool TryResolve( string                           input
                           , out ActionMetadata?             action
                           , out Dictionary<string, string>? parameters )
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
        // MODE 0.1: LLM META COMMANDS  (SetModel / SetProvider / ListModels / ListProviders)
        // Placed before every domain-specific mode so these remain reachable even when
        // the current model is exhausted and the interpreter's pre-flight would block them.
        // ------------------------------------------------------------
        if (TryResolveLlmMeta(input, out action, out parameters))
            return true;

        // ------------------------------------------------------------
        // MODE 0.5: DAILY RECORD COMMANDS  (Plan: / Check: / EOD: / ...)
        // Evaluated before colon-prefix so these explicit prefixes are not
        // accidentally swallowed by the generic journal/task colon handler.
        // ------------------------------------------------------------
        if (TryResolveDailyRecord(input, out action, out parameters))
            return true;

        if (TryResolveClaimRolledOver(input, out action, out parameters))
            return true;

        // ------------------------------------------------------------
        // MODE 1.1: EXPLICIT "<actionName>:" PREFIX (e.g. "Journal: ...")
        // ------------------------------------------------------------
        if (TryResolveColonPrefix(input, out action, out parameters))
            return true;

        // ------------------------------------------------------------
        // MODE 1.1b: MULTILINE JOURNAL BLOCK  ("Journal\nText\nTags: ...")
        // Evaluated after colon-prefix so "Journal: ..." is not re-tested.
        // ------------------------------------------------------------
        if (TryResolveJournalMultiline(input, out action, out parameters))
            return true;

        // ------------------------------------------------------------
        // MODE 1.2: PREFIX COMMANDS  (/journal ..., /task ...)
        // ------------------------------------------------------------
        if (input.StartsWith("/"))
            return TryResolvePrefix(input, out action, out parameters);

        // ------------------------------------------------------------
        // MODE 2: TASK-SPECIFIC FAST PATHS
        // Evaluated before the generic path so task intent patterns
        // are not accidentally swallowed by journal signals.
        // ------------------------------------------------------------
        if (TryResolveTaskAnalyze(input, out action, out parameters))
            return true;

        if (TryResolveTaskList(input, out action, out parameters))
            return true;

        if (TryResolveTaskCompleteBatch(input, out action, out parameters))
            return true;

        if (TryResolveTaskComplete(input, out action, out parameters))
            return true;

        if (TryResolveTaskDelete(input, out action, out parameters))
            return true;

        if (TryResolveTaskUpdatePriority(input, out action, out parameters))
            return true;

        if (TryResolveTaskUpdateDueDate(input, out action, out parameters))
            return true;

        // ------------------------------------------------------------
        // MODE 2.5: PERSONALITY-SPECIFIC FAST PATHS
        // ------------------------------------------------------------
        if (TryResolvePersonality(input, out action, out parameters))
            return true;

        // ------------------------------------------------------------
        // MODE 2.6: PERSONA-SPECIFIC FAST PATHS (alias vocabulary)
        // ------------------------------------------------------------
        if (TryResolvePersona(input, out action, out parameters))
            return true;

        // ------------------------------------------------------------
        // MODE 2.7: IDENTITY FAST PATHS
        // ------------------------------------------------------------
        if (TryResolveIdentityGetProfile(input, out action, out parameters))
            return true;

        if (TryResolveIdentityListAssertions(input, out action, out parameters))
            return true;

        // ------------------------------------------------------------
        // MODE 3: GENERAL FAST PATH (ATTRIBUTE + METADATA DRIVEN)
        // ------------------------------------------------------------
        if (TryResolveGenericFastPath(input, out action, out parameters))
            return true;

        return false;
    }

    // ================================================================
    // DAILY RECORD COMMANDS  (Plan: / Check: / EOD: / Done: / ...)
    // ================================================================

    private bool TryResolveDailyRecord( string                           input
                                      , out ActionMetadata?             action
                                      , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        if (!DailyRecordCommandParser.StartsWithKnownPrefix(input))
            return false;

        var parsed = _dailyRecordParser.Parse(input);

        if (parsed.CommandType == DailyCommandType.Unknown)
            return false;

        var actionName = parsed.CommandType switch
        {
            DailyCommandType.Plan     => "OpenDay"
          , DailyCommandType.Check    => "AddCheckpoint"
          , DailyCommandType.EndOfDay => "CloseDay"
          , _                         => string.Empty
        };

        action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == actionName);
        if (action is null) return false;

        parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        switch (parsed.CommandType)
        {
            case DailyCommandType.Plan:
                // When the user writes "Plan:\nTask..." with no opening text on the first line,
                // BodyText is empty. Substitute a minimal default so openingText is never blank.
                parameters["openingText"] = string.IsNullOrWhiteSpace(parsed.BodyText)
                                                ? "Plan"
                                                : parsed.BodyText;
                if (parsed.Tasks.Count    > 0) parameters["tasks"]     = string.Join(", ", parsed.Tasks);
                if (parsed.Mood      is not null) parameters["mood"]      = parsed.Mood;
                if (parsed.MoodScore.HasValue)    parameters["moodScore"] = parsed.MoodScore.Value.ToString();
                break;

            case DailyCommandType.Check:
                parameters["text"] = parsed.BodyText;
                if (parsed.Tasks.Count    > 0) parameters["newTasks"]  = string.Join(", ", parsed.Tasks);
                if (parsed.Mood      is not null) parameters["mood"]      = parsed.Mood;
                if (parsed.MoodScore.HasValue)    parameters["moodScore"] = parsed.MoodScore.Value.ToString();
                break;

            case DailyCommandType.EndOfDay:
                parameters["closingText"] = parsed.BodyText;
                if (parsed.Mood      is not null) parameters["mood"]      = parsed.Mood;
                if (parsed.MoodScore.HasValue)    parameters["moodScore"] = parsed.MoodScore.Value.ToString();
                break;
        }

        return true;
    }

    // ----------------------------------------------------------------
    // ClaimRolledOverTasks
    // "claim rolled-over tasks", "add rolled-over tasks to today", etc.
    // ----------------------------------------------------------------
    private static readonly string[] ClaimRolledOverSignals =
    {
            "claim rolled-over"
          , "claim rolled over"
          , "add rolled-over tasks"
          , "add rolled over tasks"
          , "bring forward rolled"
          , "claim yesterday's unfinished"
          , "claim unfinished tasks"
    };

    private bool TryResolveClaimRolledOver( string                           input
                                           , out ActionMetadata?             action
                                           , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        var normalized = input.ToLowerInvariant();

        if (ClaimRolledOverSignals.Any(signal => normalized.Contains(signal)).Not())
            return false;

        action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "ClaimRolledOverTasks");
        if (action is null) return false;

        parameters = new Dictionary<string, string>();
        return true;
    }

    
    // ================================================================
    // COLON PREFIX MODE  (journal: ..., task: ...)
    // ================================================================
    private bool TryResolveColonPrefix( string                           input
                                      , out ActionMetadata?             action
                                      , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        var colonIndex = input.IndexOf(':');
        if (colonIndex <= 0) return false;

        var prefix = input[..colonIndex].Trim();
        if (string.IsNullOrWhiteSpace(prefix)) return false;

        if (prefix.Equals("journal", StringComparison.OrdinalIgnoreCase))
        {
            action = _registry.Actions.FirstOrDefault(action => action.Name == "AddJournalEntry");
            if (action is null) return false;

            var body = input[(colonIndex + 1)..].Trim();
            parameters = new Dictionary<string, string> { ["text"] = body };

            return true;
        }

        if (prefix.Equals("task", StringComparison.OrdinalIgnoreCase))
        {
            action = _registry.Actions.FirstOrDefault(action => action.Name == "AddTask");
            if (action is null) return false;

            input      = input.Replace("task:", "shortDescription:", StringComparison.OrdinalIgnoreCase);
            parameters = ParseToDictionary(input);

            // "DueDate:" is a natural alias users write in the block, but the action
            // parameter is "dueDateText". Remap so it isn't silently dropped.
            if (parameters.Remove("DueDate", out var dueDateValue))
                parameters["dueDateText"] = dueDateValue;

            return true;
        }

        if (prefix.Equals("system", StringComparison.OrdinalIgnoreCase))
        {
            SetSystemAction(input:  input
                          , action: ref action);
            
            parameters = ParseToDictionary(input);
            
            return action is not null;
        }

        return false;
    }

    private void SetSystemAction( string              input
                                , ref ActionMetadata? action )
    {

        if (input.Contains("version"))       action = _registry.Actions.FirstOrDefault(action => action.Name == "GetVersion");
        if (input.Contains("environment"))   action = _registry.Actions.FirstOrDefault(action => action.Name == "GetEnvironment");
        if (input.Contains("uptime"))        action = _registry.Actions.FirstOrDefault(action => action.Name == "GetUptime");
        if (input.Contains("System Status")) action = _registry.Actions.FirstOrDefault(action => action.Name == "GetSystemStatus");
        if (input.Contains("configuration")) action = _registry.Actions.FirstOrDefault(action => action.Name == "GetConfigurationValue");
    }

    public static Dictionary<string, string> ParseToDictionary(string input)
    {
        return input.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(line => line.Split(':', 2, StringSplitOptions.TrimEntries))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
    }

    // ================================================================
    // MULTILINE JOURNAL BLOCK  ("Journal\nText\nTags: ..." ...)
    // Handles the structured block format where "Journal" is the sole
    // first line, followed by free-text and optional key-value lines.
    // ================================================================
    private bool TryResolveJournalMultiline( string                           input
                                           , out ActionMetadata?             action
                                           , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        var lines = input.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length < 2)                                                    return false;
        if (!lines[0].Equals("Journal", StringComparison.OrdinalIgnoreCase))    return false;

        action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "AddJournalEntry");
        if (action is null) return false;

        parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var bodyLines = lines.Skip(1).ToList();

        // First line that has no colon is the free-text body.
        var textLine = bodyLines.FirstOrDefault(line => line.Contains(':').Not());
        if (textLine is not null)
            parameters["text"] = textLine;

        foreach (var line in bodyLines.Where(line => line.Contains(':')))
        {
            var parts = line.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2) continue;

            var value = parts[1].Trim();

            switch (parts[0].ToLowerInvariant())
            {
                case "tags":
                    parameters["tags"] = string.Join(",", value.Split(',')
                                                                .Select(tag => tag.Trim().Trim('"')));
                    break;
                case "mood":
                    parameters["mood"] = value.Trim('"');
                    break;
                case "moodscore":
                    parameters["moodScore"] = value.Trim('"');
                    break;
            }
        }

        return parameters.ContainsKey("text");
    }

    // ================================================================
    // PREFIX COMMAND MODE  (/journal ..., /task ...)
    // ================================================================
    private bool TryResolvePrefix( string                           input
                                  , out ActionMetadata?             action
                                  , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        var parts  = input.Substring(1).Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        var domain = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;

        if (domain == "journal")
            return TryResolvePrefixJournal(parts, out action, out parameters);

        if (domain == "task")
            return TryResolvePrefixTask(parts, out action, out parameters);

        if (domain == "personality")
            return TryResolvePrefixPersonality(parts, out action, out parameters);

        if (domain == "persona")
            return TryResolvePrefixPersona(parts, out action, out parameters);

        return false;
    }

    private bool TryResolvePrefixJournal( string[]                         parts
                                         , out ActionMetadata?             action
                                         , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        if (parts.Length < 2) return false;

        var verb = parts[1].ToLowerInvariant();

        if (verb == "add" && parts.Length == 3)
        {
            action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "AddJournalEntry");
            if (action is null) return false;

            parameters = new Dictionary<string, string> { ["text"] = parts[2] };
            return true;
        }

        if (verb == "history")
        {
            action     = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "JournalEntriesOnThisDay");
            parameters = new Dictionary<string, string>();
            return action != null;
        }

        if (verb == "list")
        {
            action     = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "ListJournalEntries");
            parameters = new Dictionary<string, string>();
            return action != null;
        }

        return false;
    }

    // ----------------------------------------------------------------
    // /task <verb> [arg]
    //
    //   /task add <description>
    //   /task list
    //   /task list urgent
    //   /task list important
    //   /task list overdue
    //   /task complete <ref>
    //   /task done <ref>
    //   /task delete <ref>
    //   /task remove <ref>
    //   /task analyze
    //   /task prioritize
    // ----------------------------------------------------------------
    private bool TryResolvePrefixTask( string[]                         parts
                                      , out ActionMetadata?             action
                                      , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        if (parts.Length < 2) return false;

        var verb = parts[1].ToLowerInvariant();
        var arg  = parts.Length == 3 ? parts[2] : string.Empty;

        switch (verb)
        {
            case "add":
            {
                if (string.IsNullOrWhiteSpace(arg)) return false;

                action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "AddTask");
                if (action is null) return false;

                parameters = new Dictionary<string, string> { ["shortDescription"] = arg };
                return true;
            }

            case "list":
            {
                action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "ListTasks");
                if (action is null) return false;

                var listParams = BuildListTasksParameters(arg.ToLowerInvariant());

                // "/task list" with no qualifier means "show everything" â€" same
                // contract as "show my tasks" in natural language.
                if (string.IsNullOrWhiteSpace(arg))
                    listParams["includeCompleted"] = "true";

                parameters = listParams;
                return true;
            }

            case "complete":
            case "done":
            {
                if (string.IsNullOrWhiteSpace(arg)) return false;

                action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "CompleteTask");
                if (action is null) return false;

                parameters = new Dictionary<string, string> { ["taskReference"] = arg };
                return true;
            }

            case "delete":
            case "remove":
            {
                if (string.IsNullOrWhiteSpace(arg)) return false;

                action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "DeleteTask");
                if (action is null) return false;

                parameters = new Dictionary<string, string> { ["taskReference"] = arg };
                return true;
            }

            case "due":
            {
                // /task due <ref> <date>  e.g. "/task due 2 Friday"
                // Split arg into reference (first token) and date text (remainder).
                if (string.IsNullOrWhiteSpace(arg)) return false;

                var dueParts   = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var dueRef     = dueParts[0];
                var dueDateArg = dueParts.Length == 2 ? dueParts[1] : string.Empty;

                if (string.IsNullOrWhiteSpace(dueDateArg)) return false;

                action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "UpdateTaskDueDate");
                if (action is null) return false;

                parameters = new Dictionary<string, string>
                             {
                                     ["taskReference"] = dueRef
                                   , ["dueDateText"]   = dueDateArg
                             };
                return true;
            }

            case "analyze":
            case "prioritize":
            {
                action     = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "AnalyzeTasks");
                parameters = new Dictionary<string, string>();
                return action != null;
            }
        }

        return false;
    }

    // ================================================================
    // TASK-SPECIFIC FAST PATHS (natural language)
    // ================================================================

    // ----------------------------------------------------------------
    // AnalyzeTasks
    // "prioritize my tasks", "analyze my workload", "eisenhower matrix"
    // ----------------------------------------------------------------
    private static readonly string[] AnalyzeTaskSignals =
    {
            "prioritize my tasks"
          , "analyze my tasks"
          , "analyze my workload"
          , "analyze tasks"
          , "eisenhower"
          , "which tasks should i do first"
          , "what should i work on first"
          , "show my task matrix"
          , "task matrix"
    };

    private bool TryResolveTaskAnalyze( string                           input
                                       , out ActionMetadata?             action
                                       , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        var normalized = input.ToLowerInvariant().TrimEnd('?', '!', '.');

        if (AnalyzeTaskSignals.Any(signal => normalized.Contains(signal)).Not())
            return false;

        action     = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "AnalyzeTasks");
        parameters = new Dictionary<string, string>();

        return action != null;
    }

    // ----------------------------------------------------------------
    // ListTasks
    // "show my tasks", "list tasks", "show urgent tasks", "show overdue tasks"
    // ----------------------------------------------------------------
    private static readonly string[] ListTaskSignals =
    {
            "show my tasks"
          , "show tasks"
          , "list my tasks"
          , "list tasks"
          , "what are my tasks"
          , "what tasks do i have"
          , "show all tasks"
          , "show completed tasks"
          , "show open tasks"
          , "show urgent tasks"
          , "show important tasks"
          , "show overdue tasks"
          , "show high priority tasks"
          , "show critical tasks"
          , "tasks due today"
          , "tasks due tomorrow"
          , "tasks this week"
    };

    private bool TryResolveTaskList( string                           input
                                    , out ActionMetadata?             action
                                    , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        var normalized = input.ToLowerInvariant().TrimEnd('?', '!', '.');

        if (ListTaskSignals.Any(signal => normalized.Contains(signal)).Not())
            return false;

        action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "ListTasks");
        if (action is null) return false;

        parameters = BuildListTasksParameters(normalized);

        return true;
    }

    /// <summary>
    /// Extracts ListTasks filter parameters from a normalized (lowercased) input string.
    /// Called both from natural-language detection and from the /task list prefix command.
    /// </summary>
    private static Dictionary<string, string> BuildListTasksParameters(string normalized)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // "show my tasks" / "list my tasks" / "what are my tasks" â€" no qualifier means
        // the user wants their full picture: open AND completed (but not deleted).
        // Explicit filter phrases like "show open tasks" leave includeCompleted unset (false).
        var unqualifiedSignals    = new[] { "show my tasks", "list my tasks", "what are my tasks"
                                               , "what tasks do i have", "show all tasks" };
        var isUnqualifiedTaskList = unqualifiedSignals.Any(signal => normalized.Contains(signal));
        if (isUnqualifiedTaskList)
            parameters["includeCompleted"] = "true";

        if (normalized.Contains("completed") || (normalized.Contains("done") && normalized.Contains("show")))
            parameters["includeCompleted"] = "true";

        if (normalized.Contains("urgent"))
            parameters["onlyUrgent"] = "true";

        if (normalized.Contains("important"))
            parameters["onlyImportant"] = "true";

        if (normalized.Contains("overdue"))
            parameters["overdueOnly"] = "true";

        // Due-date shorthand phrases â€" translate to a dueBefore value the execution engine
        // can coerce to DateTimeOffset?. Full ISO-8601 with offset is used deliberately:
        // date-only "yyyy-MM-dd" may be parsed as a local DateTime rather than a DateTimeOffset,
        // causing silent coercion failure and the filter being ignored entirely.
        if (normalized.Contains("due today"))
            parameters["dueBefore"] = DateTimeOffset.UtcNow.Date.AddDays(1).ToString("yyyy-MM-ddTHH:mm:sszzz");

        if (normalized.Contains("due tomorrow"))
            parameters["dueBefore"] = DateTimeOffset.UtcNow.Date.AddDays(2).ToString("yyyy-MM-ddTHH:mm:sszzz");

        if (normalized.Contains("this week"))
            parameters["dueBefore"] = DateTimeOffset.UtcNow.Date.AddDays(7).ToString("yyyy-MM-ddTHH:mm:sszzz");

        // Priority keyword â†’ map to the equivalent importance/urgency flags so
        // ListTasks' existing filter parameters are satisfied without LLM help.
        if (normalized.Contains("high priority") || normalized.Contains("critical"))
        {
            parameters["onlyImportant"] = "true";
            parameters["onlyUrgent"]    = "true";
        }

        return parameters;
    }

    // ----------------------------------------------------------------
    // CompleteTask (single)
    // "task 3 is done", "complete task 2", "mark task 4 as done", "task 1 done"
    // ----------------------------------------------------------------
    private static readonly string[] CompleteTaskSignals =
    {
            "task"
          , "complete task"
          , "mark task"
          , "finish task"
          , "finished task"
          , "done with task"
    };

    private bool TryResolveTaskComplete( string                           input
                                        , out ActionMetadata?             action
                                        , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        var normalized = input.ToLowerInvariant();

        // Must contain a completion signal AND a single task number.
        // Batch detection runs first in TryResolve, so if we reach here
        // the input is already confirmed to be a single-task reference.
        var completionWords        = new[] { "done", "complete", "completed", "finish", "finished" };
        var containsCompletionWord = completionWords.Any(word => normalized.Contains(word));

        if (containsCompletionWord.Not())
            return false;

        if (CompleteTaskSignals.Any(signal => normalized.Contains(signal)).Not())
            return false;

        var reference = ExtractSingleTaskReference(normalized);
        if (reference is null) return false;

        action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "CompleteTask");
        if (action is null) return false;

        parameters = new Dictionary<string, string> { ["taskReference"] = reference };
        return true;
    }

    // ----------------------------------------------------------------
    // CompleteBatch
    // "tasks 2 and 4 are done", "mark tasks 1, 3, and 5 as complete"
    // ----------------------------------------------------------------
    private bool TryResolveTaskCompleteBatch( string                           input
                                             , out ActionMetadata?             action
                                             , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        var normalized = input.ToLowerInvariant();

        // Must look like a multi-task completion intent.
        var completionWords   = new[] { "done", "complete", "completed", "finish", "finished" };
        var hasTasksPlural    = normalized.Contains("tasks");
        var hasCompletionWord = completionWords.Any(word => normalized.Contains(word));

        if (hasTasksPlural.Not() || hasCompletionWord.Not())
            return false;

        var refs = ExtractAllTaskReferences(normalized);

        if (refs.Count < 2) return false;

        action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "CompleteBatch");
        if (action is null) return false;

        parameters = new Dictionary<string, string> { ["taskReferences"] = string.Join("|", refs) };
        return true;
    }

    // ----------------------------------------------------------------
    // DeleteTask
    // "delete task 3", "remove task 2 from my list"
    // ----------------------------------------------------------------
    private static readonly string[] DeleteTaskSignals =
    {
            "delete task"
          , "remove task"
          , "drop task"
          , "get rid of task"
          , "cancel task"
    };

    private bool TryResolveTaskDelete( string                           input
                                      , out ActionMetadata?             action
                                      , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        var normalized = input.ToLowerInvariant();

        if (DeleteTaskSignals.Any(signal => normalized.Contains(signal)).Not())
            return false;

        var reference = ExtractSingleTaskReference(normalized);
        if (reference is null) return false;

        action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "DeleteTask");
        if (action is null) return false;

        parameters = new Dictionary<string, string> { ["taskReference"] = reference };
        return true;
    }

    // ----------------------------------------------------------------
    // UpdateTaskPriority
    // "make task 1 high priority", "task 3 is urgent", "mark task 2 not important"
    // "set task 4 to critical", "task 1 is important and urgent"
    // ----------------------------------------------------------------
    private static readonly string[] UpdatePrioritySignals =
    {
            "make task"
          , "set task"
          , "mark task"
          , "task is urgent"
          , "task is important"
          , "task is not urgent"
          , "task is not important"
          , "is urgent"
          , "is important"
          , "is not urgent"
          , "is not important"
          , "high priority"
          , "low priority"
          , "critical"
          , "normal priority"
    };

    private bool TryResolveTaskUpdatePriority( string                           input
                                              , out ActionMetadata?             action
                                              , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        var normalized = input.ToLowerInvariant();

        if (UpdatePrioritySignals.Any(signal => normalized.Contains(signal)).Not())
            return false;

        var reference = ExtractSingleTaskReference(normalized);
        if (reference is null) return false;

        // Only proceed if we can extract at least one meaningful update.
        var extracted = ExtractPriorityParameters(normalized);
        if (extracted.Count == 0) return false;

        action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "UpdateTaskPriority");
        if (action is null) return false;

        extracted["taskReference"] = reference;
        parameters                 = extracted;

        return true;
    }

    /// <summary>
    /// Extracts priority, isImportant, and isUrgent values from a normalized input string.
    /// Returns an empty dictionary if no recognizable priority signals are found.
    /// </summary>
    private static Dictionary<string, string> ExtractPriorityParameters(string normalized)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // --- Priority level ---
        if (normalized.Contains("critical"))
            result["priority"] = "Critical";
        else if (normalized.Contains("high priority") || normalized.Contains("high-priority"))
            result["priority"] = "High";
        else if (normalized.Contains("low priority") || normalized.Contains("low-priority"))
            result["priority"] = "Low";
        else if (normalized.Contains("normal priority"))
            result["priority"] = "Normal";

        // --- Urgency ---
        // "not urgent" must be tested before "urgent" to avoid false positives.
        if (normalized.Contains("not urgent") || normalized.Contains("isn't urgent") || normalized.Contains("no longer urgent"))
            result["isUrgent"] = "false";
        else if (normalized.Contains("urgent"))
            result["isUrgent"] = "true";

        // --- Importance ---
        // Same negative-first ordering.
        if (normalized.Contains("not important") || normalized.Contains("isn't important") || normalized.Contains("no longer important"))
            result["isImportant"] = "false";
        else if (normalized.Contains("important"))
            result["isImportant"] = "true";

        return result;
    }

    // ----------------------------------------------------------------
    // UpdateTaskDueDate
    // "task 2 is due Friday", "set task 3 due date to April 5th",
    // "task 1 is due tomorrow", "clear due date on task 4"
    // ----------------------------------------------------------------
    private static readonly string[] UpdateDueDateSignals =
    {
            "is due"
          , "due date"
          , "due on"
          , "due by"
          , "set due"
          , "change due"
          , "update due"
          , "move due"
          , "clear due"
          , "remove due"
          , "no due date"
    };

    private bool TryResolveTaskUpdateDueDate( string                           input
                                             , out ActionMetadata?             action
                                             , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        var normalized = input.ToLowerInvariant();

        if (UpdateDueDateSignals.Any(signal => normalized.Contains(signal)).Not())
            return false;

        var reference = ExtractSingleTaskReference(normalized);
        if (reference is null) return false;

        action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "UpdateTaskDueDate");
        if (action is null) return false;

        // Clearing phrases â€" pass a sentinel the action recognises as "remove due date".
        var isClearIntent = normalized.Contains("clear due")
                         || normalized.Contains("remove due")
                         || normalized.Contains("no due date");

        if (isClearIntent)
        {
            parameters = new Dictionary<string, string>
                         {
                                 ["taskReference"] = reference
                               , ["dueDateText"]   = string.Empty
                         };
            return true;
        }

        // Extract the date text that follows the signal phrase.
        var dateText = ExtractDueDateText(normalized);
        if (string.IsNullOrWhiteSpace(dateText)) return false;

        parameters = new Dictionary<string, string>
                     {
                             ["taskReference"] = reference
                           , ["dueDateText"]   = dateText
                     };
        return true;
    }

    /// <summary>
    /// Extracts the raw date string that follows a due-date signal phrase.
    /// e.g. "task 2 is due friday" â†’ "friday"
    ///      "set task 3 due date to april 5th" â†’ "april 5th"
    /// </summary>
    private static string ExtractDueDateText(string normalized)
    {
        // Ordered from most specific to least so we consume the longest match first.
        var dueDatePhrases = new[]
        {
                "due date to "
              , "due date on "
              , "due date by "
              , "due date "
              , "due on "
              , "due by "
              , "is due "
              , "set due "
              , "due "
        };

        foreach (var phrase in dueDatePhrases)
        {
            var index = normalized.IndexOf(phrase, StringComparison.Ordinal);
            if (index < 0) continue;

            var candidate = normalized.Substring(index + phrase.Length).Trim();

            // Strip leading filler words ("to", "for", "at") that may precede the date.
            foreach (var filler in new[] { "to ", "for ", "at " })
            {
                if (candidate.StartsWith(filler))
                    candidate = candidate.Substring(filler.Length).Trim();
            }

            if (candidate.Length > 0)
                return candidate;
        }

        return string.Empty;
    }

    // ================================================================
    // MODE 2.5: PERSONALITY FAST PATHS
    // ================================================================

    private static readonly string[] ListPersonalitySignals =
    {
            "list personalities"
          , "show personalities"
          , "show personality options"
          , "list all personalities"
          , "show available personalities"
          , "what personalities are available"
          , "what personalities do you have"
    };

    private static readonly string[] GetActivePersonalitySignals =
    {
            "what personality is active"
          , "which personality am i using"
          , "show the current personality"
          , "current personality"
          , "active personality"
          , "what personality are you using"
          , "what personality is set"
    };

    private static readonly string[] SetPersonalityPrefixes =
    {
            "switch to the "
          , "switch to "
          , "use the "
          , "use personality "
          , "set personality to "
          , "change personality to "
          , "change to "
          , "activate "
    };

    private bool TryResolvePersonality( string                           input
                                      , out ActionMetadata?             action
                                      , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        var normalized = input.ToLowerInvariant().TrimEnd('?', '!', '.');

        if (ListPersonalitySignals.Any(signal => normalized.Contains(signal)))
        {
            action     = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "ListPersonalities");
            parameters = new Dictionary<string, string>();
            return action != null;
        }

        if (GetActivePersonalitySignals.Any(signal => normalized.Contains(signal)))
        {
            action     = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "GetActivePersonality");
            parameters = new Dictionary<string, string>();
            return action != null;
        }

        var extractedName = ExtractPersonalityName(normalized);
        if (extractedName is not null)
        {
            action     = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "SetPersonality");
            if (action is null) return false;

            parameters = new Dictionary<string, string> { ["name"] = extractedName };
            return true;
        }

        return false;
    }

    // ----------------------------------------------------------------
    // /personality <verb> [arg]
    //   /personality list
    //   /personality active
    //   /personality set <name>
    //   /personality use <name>
    // ----------------------------------------------------------------
    private bool TryResolvePrefixPersonality( string[]                         parts
                                            , out ActionMetadata?             action
                                            , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        if (parts.Length < 2) return false;

        var verb = parts[1].ToLowerInvariant();
        var arg  = parts.Length == 3 ? parts[2] : string.Empty;

        switch (verb)
        {
            case "list":
            {
                action     = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "ListPersonalities");
                parameters = new Dictionary<string, string>();
                return action != null;
            }

            case "active":
            {
                action     = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "GetActivePersonality");
                parameters = new Dictionary<string, string>();
                return action != null;
            }

            case "set":
            case "use":
            case "switch":
            {
                if (string.IsNullOrWhiteSpace(arg)) return false;

                action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "SetPersonality");
                if (action is null) return false;

                parameters = new Dictionary<string, string> { ["name"] = arg };
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts a personality name from natural-language set-personality phrases.
    /// Returns null if no recognized set-personality prefix is found.
    /// </summary>
    private static string? ExtractPersonalityName(string normalized)
    {
        foreach (var prefix in SetPersonalityPrefixes)
        {
            if (!normalized.StartsWith(prefix)) continue;

            var candidate = normalized.Substring(prefix.Length).Trim();

            // Strip trailing "personality" or "persona" qualifier
            // (e.g. "switch to the zen personality" / "switch to the zen persona")
            if (candidate.EndsWith(" personality"))
                candidate = candidate[..^" personality".Length].Trim();
            else if (candidate.EndsWith(" persona"))
                candidate = candidate[..^" persona".Length].Trim();

            if (candidate.Length > 0)
                return candidate;
        }

        return null;
    }

    // ================================================================
    // MODE 2.6: PERSONA FAST PATHS (alias vocabulary for "persona" phrasing)
    // ================================================================

    private static readonly string[] ListPersonaSignals =
    {
            "list personas"
          , "show personas"
          , "show persona options"
          , "list all personas"
          , "show available personas"
          , "what personas are available"
          , "what personas do you have"
    };

    private static readonly string[] GetActivePersonaSignals =
    {
            "what persona is active"
          , "which persona am i using"
          , "show the current persona"
          , "current persona"
          , "active persona"
          , "what persona are you using"
          , "what persona is set"
          , "what's my persona"
    };

    private static readonly string[] SetPersonaPrefixes =
    {
            "set persona to "
          , "set my persona to "
          , "change persona to "
          , "use persona "
          , "switch persona to "
    };

    private bool TryResolvePersona( string                           input
                                  , out ActionMetadata?             action
                                  , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        var normalized = input.ToLowerInvariant().TrimEnd('?', '!', '.');

        if (ListPersonaSignals.Any(signal => normalized.Contains(signal)))
        {
            action     = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "ListPersonas");
            parameters = new Dictionary<string, string>();
            return action != null;
        }

        if (GetActivePersonaSignals.Any(signal => normalized.Contains(signal)))
        {
            action     = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "GetActivePersona");
            parameters = new Dictionary<string, string>();
            return action != null;
        }

        var extractedName = ExtractPersonaName(normalized);
        if (extractedName is not null)
        {
            action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "SetPersona");
            if (action is null) return false;

            parameters = new Dictionary<string, string> { ["name"] = extractedName };
            return true;
        }

        return false;
    }

    // ----------------------------------------------------------------
    // /persona <verb> [arg]
    //   /persona list
    //   /persona active
    //   /persona set <name>
    //   /persona use <name>
    // ----------------------------------------------------------------
    private bool TryResolvePrefixPersona( string[]                         parts
                                        , out ActionMetadata?             action
                                        , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        if (parts.Length < 2) return false;

        var verb = parts[1].ToLowerInvariant();
        var arg  = parts.Length == 3 ? parts[2] : string.Empty;

        switch (verb)
        {
            case "list":
            {
                action     = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "ListPersonas");
                parameters = new Dictionary<string, string>();
                return action != null;
            }

            case "active":
            {
                action     = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "GetActivePersona");
                parameters = new Dictionary<string, string>();
                return action != null;
            }

            case "set":
            case "use":
            case "switch":
            {
                if (string.IsNullOrWhiteSpace(arg)) return false;

                action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "SetPersona");
                if (action is null) return false;

                parameters = new Dictionary<string, string> { ["name"] = arg };
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts a persona name from natural-language set-persona phrases.
    /// Returns null if no recognized set-persona prefix is found.
    /// </summary>
    private static string? ExtractPersonaName(string normalized)
    {
        foreach (var prefix in SetPersonaPrefixes)
        {
            if (!normalized.StartsWith(prefix)) continue;

            var candidate = normalized.Substring(prefix.Length).Trim();

            // Strip trailing "persona" qualifier if present (e.g. "set persona to zen persona")
            if (candidate.EndsWith(" persona"))
                candidate = candidate[..^" persona".Length].Trim();

            if (candidate.Length > 0)
                return candidate;
        }

        return null;
    }

    // ================================================================
    // MODE 3: GENERIC FAST PATH — ATTRIBUTE + METADATA DRIVEN
    // ================================================================
    private bool TryResolveGenericFastPath( string                           input
                                           , out ActionMetadata?             action
                                           , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        var normalized = input.ToLowerInvariant();

        // Guard: inputs that open with a destructive verb must fall through to
        // the LLM so the orchestrator's IsDestructive confirmation flow can run.
        if (StartsWithDestructiveVerb(normalized))
            return false;

        // Guard: if the input looks like a batch task list, do not fast-path it.
        // Let the LLM handle it so BatchAddTasks can be selected instead.
        if (IsBatchIntent(normalized))
            return false;

        var candidates = _registry.FastPathActions;

        if (candidates.Any().Not())
            return false;

        foreach (var meta in candidates)
        {
            var requiredParams = meta.Parameters
                                     .Where(parameter => parameter.IsOptional.Not())
                                     .ToList();

            if (requiredParams.Count != 1)
                continue;

            var mainParam = requiredParams.Single();

            if (ContainsFastPathSignal(normalized).Not()) continue;

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
    // DESTRUCTIVE VERB GUARD
    // Inputs that begin with a destructive verb should not be resolved
    // by the generic fast path. They fall through to the LLM so that
    // the orchestrator's IsDestructive confirmation flow can engage.
    // ================================================================
    private static readonly string[] DestructiveVerbPrefixes =
    {
            "delete "
          , "remove "
          , "drop "
          , "erase "
          , "clear "
          , "purge "
    };

    private static bool StartsWithDestructiveVerb(string normalized)
        => DestructiveVerbPrefixes.Any(normalized.StartsWith);

    // ================================================================
    // BATCH INTENT GUARD
    // Signals that the user is describing multiple tasks at once.
    // When any of these are present, skip fast-path so the LLM can
    // route to BatchAddTasks instead of AddTask.
    // ================================================================
    private static readonly string[] BatchIntentSignals =
    {
            "several things"
          , "a few things"
          , "these tasks:"
          , "these tasks :"
          , "add tasks:"
          , "add tasks :"
          , "create tasks:"
          , "create tasks :"
          , ", and "
          , ": "    // colon-separated list pattern (e.g. "need to do: x, y, z")
    };

    private static bool IsBatchIntent(string normalizedInput)
    {
        // A single comma alone is not enough (e.g. "add task finish report, friday").
        // Require at least two commas OR a known batch signal phrase.
        var commaCount = normalizedInput.Count(character => character == ',');

        if (commaCount >= 2)
            return true;

        return BatchIntentSignals.Any(signal => normalizedInput.Contains(signal));
    }

    // ================================================================
    // SIGNAL DETECTION â€" NATURAL LANGUAGE INTENT MARKERS (journal/add)
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
    };

    private static bool ContainsFastPathSignal(string input)
        => FastPathSignals.Any(signal => input.Contains(signal));

    // ================================================================
    // PRIMARY VALUE EXTRACTION (journal/add generic path)
    // ================================================================
    private static string ExtractPrimaryValue(string input)
    {
        foreach (var signal in FastPathSignals)
        {
            if (input.DoesNotContainOrNull(signal)) continue;

            var pos = input.IndexOf(signal, StringComparison.Ordinal);
            if (pos >= 0)
                return input.Substring(pos + signal.Length).Trim();
        }

        return string.Empty;
    }

    // ================================================================
    // TASK REFERENCE EXTRACTION HELPERS
    // ================================================================

    // Matches a standalone integer: "task 3", "tasks 2 and 4", "delete task 12"
    private static readonly Regex TaskReferencePattern = new(@"\b(\d+)\b", RegexOptions.Compiled);

    /// <summary>
    /// Extracts a single task position or id from the input.
    /// Returns null if zero or more than one number is found (use
    /// <see cref="ExtractAllTaskReferences"/> for batch inputs).
    /// </summary>
    private static string? ExtractSingleTaskReference(string normalized)
    {
        var matches = TaskReferencePattern.Matches(normalized);

        return matches.Count == 1
                       ? matches[0].Value
                       : null;
    }

    /// <summary>
    /// Extracts all task position numbers from a normalized input string,
    /// preserving the order they appear in. Used by CompleteBatch.
    /// </summary>
    private static List<string> ExtractAllTaskReferences(string normalized)
    {
        return TaskReferencePattern.Matches(normalized)
                                   .Select(match => match.Value)
                                   .Distinct()
                                   .ToList();
    }

    // ================================================================
    // CAPABILITIES QUERY
    // ================================================================
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

    // ================================================================
    // LLM META COMMANDS  (SetModel / SetProvider / ListModels / ListProviders)
    // ================================================================

    /// <summary>
    /// Routes LLM model- and provider-switching commands without requiring an LLM call.
    /// Because FastPath runs before <see cref="LlmInterpreter"/> the interpreter's
    /// catalog pre-flight is never reached, so these commands work even when every
    /// known model is exhausted or unavailable.
    /// </summary>
    private bool TryResolveLlmMeta( string                           input
                                   , out ActionMetadata?             action
                                   , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        // SetModel â€" requires explicit "model" keyword to avoid false positives
        var setModelMatch = Regex.Match(input,
                                       @"^(?:set\s+model\s+to|use\s+model|switch\s+model\s+to|change\s+model\s+to|switch\s+to\s+model)\s+(.+)$",
                                       RegexOptions.IgnoreCase);
        if (setModelMatch.Success)
        {
            action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "SetModel");
            if (action is null) return false;
            parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                         { { "model", setModelMatch.Groups[1].Value.Trim() } };
            return true;
        }

        // SetProvider â€" explicit "provider" keyword
        var setProviderMatch = Regex.Match(input,
                                           @"^(?:set\s+provider\s+to|use\s+provider|switch\s+provider\s+to|switch\s+to\s+provider)\s+(.+)$",
                                           RegexOptions.IgnoreCase);
        if (setProviderMatch.Success)
        {
            action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "SetProvider");
            if (action is null) return false;
            parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                         { { "provider", setProviderMatch.Groups[1].Value.Trim() } };
            return true;
        }

        // "switch to <X>" or "use <X>" â€" only when X is a known LlmProvider name
        var bareSwitch = Regex.Match(input, @"^(?:switch\s+to|use)\s+(\w+)$", RegexOptions.IgnoreCase);
        if (bareSwitch.Success && Enum.TryParse<LlmProvider>(bareSwitch.Groups[1].Value, ignoreCase: true, out _))
        {
            action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "SetProvider");
            if (action is null) return false;
            parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                         { { "provider", bareSwitch.Groups[1].Value.Trim() } };
            return true;
        }

        // ListModels
        if (Regex.IsMatch(input, @"^(?:list|show|what|available)\s+models\b", RegexOptions.IgnoreCase)
         || Regex.IsMatch(input, @"^what\s+models\s+are\s+available", RegexOptions.IgnoreCase))
        {
            action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "ListModels");
            if (action is null) return false;
            parameters = new Dictionary<string, string>();
            return true;
        }

        // ListProviders
        if (Regex.IsMatch(input, @"^(?:list|show|what|available)\s+providers\b", RegexOptions.IgnoreCase)
         || Regex.IsMatch(input, @"^what\s+providers\s+are\s+available", RegexOptions.IgnoreCase))
        {
            action = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "ListProviders");
            if (action is null) return false;
            parameters = new Dictionary<string, string>();
            return true;
        }

        return false;
    }

    // ================================================================
    // WORKSPACE PREFIX  ("workspaceName: remainder")
    // ================================================================

    private static readonly Regex WorkspacePrefixPattern =
        new(@"^([A-Za-z][A-Za-z0-9_-]*):\s+(.+)$"
          , RegexOptions.Singleline | RegexOptions.Compiled);

    // ================================================================
    // IDENTITY FAST PATHS
    // ================================================================

    // ----------------------------------------------------------------
    // GetProfile
    // "show my profile", "get my profile", "my profile", "who am i"
    // ----------------------------------------------------------------
    private static readonly string[] IdentityGetProfileSignals =
    {
            "show my profile"
          , "get my profile"
          , "display my profile"
          , "view my profile"
          , "show my identity profile"
          , "who am i"
          , "show who i am"
    };

    private bool TryResolveIdentityGetProfile( string                           input
                                              , out ActionMetadata?             action
                                              , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        var normalized = input.ToLowerInvariant().TrimEnd('?', '!', '.');

        if (IdentityGetProfileSignals.Any(signal => normalized.Contains(signal)).Not())
            return false;

        action     = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "GetProfile");
        parameters = new Dictionary<string, string>();

        return action != null;
    }

    // ----------------------------------------------------------------
    // ListIdentityAssertions
    // "show my identity assertions", "list assertions", "identity facts"
    // ----------------------------------------------------------------
    private static readonly string[] IdentityListAssertionsSignals =
    {
            "identity assertions"
          , "my assertions"
          , "list assertions"
          , "show assertions"
          , "identity facts"
    };

    private bool TryResolveIdentityListAssertions( string                           input
                                                  , out ActionMetadata?             action
                                                  , out Dictionary<string, string>? parameters )
    {
        action     = null;
        parameters = null;

        var normalized = input.ToLowerInvariant().TrimEnd('?', '!', '.');

        if (IdentityListAssertionsSignals.Any(signal => normalized.Contains(signal)).Not())
            return false;

        action     = _registry.Actions.FirstOrDefault(registryAction => registryAction.Name == "ListIdentityAssertions");
        parameters = new Dictionary<string, string>();

        return action != null;
    }

    /// <inheritdoc />
    public bool TryExtractWorkspacePrefix( string     input
                                         , out string? workspaceName
                                         , out string? remainder )
    {
        workspaceName = null;
        remainder     = null;

        if (input.HasValue().Not()) return false;

        var match = WorkspacePrefixPattern.Match(input.Trim());
        if (!match.Success) return false;

        var token = match.Groups[1].Value;

        // Built-in daily-record prefixes (Plan:, Check:, EOD:, Done:, Evening:, DayDone:)
        // must not be treated as workspace names.
        if (DailyRecordCommandParser.StartsWithKnownPrefix(input)) return false;

        // Registered action names (Journal:, AddTask:, etc.) are handled by Mode 1.1.
        if (_registry.Actions.Any(registryAction =>
                registryAction.Name.Equals(token, StringComparison.OrdinalIgnoreCase)))
            return false;
        
        if (_registry.Actions.Any(registryAction =>
                registryAction.Category.Equals(token, StringComparison.OrdinalIgnoreCase)))
            return false;
        
        workspaceName = token;
        remainder     = match.Groups[2].Value.Trim();
        return true;
    }
}