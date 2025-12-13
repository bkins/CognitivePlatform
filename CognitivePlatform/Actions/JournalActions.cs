using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Domains.Journal;

namespace CognitivePlatform.Api.Actions;

[Category("journal")]
public sealed class JournalActions
{
    private readonly JournalService _journal;

    public JournalActions(JournalService journal)
    {
        _journal = journal;
    }

    // ----------------------------------------------------------------------
    // 1. AddJournalEntry
    // ----------------------------------------------------------------------
    [NaturalLanguageAction(Description = "Adds a new journal entry with optional tags or context."
                         , Examples = new[]
                                      {
                                              "Write in my journal that I felt anxious today."
                                            , "Add a journal entry saying I had a productive day."
                                            , "Record that I exercised this morning."
                                            , "Write a journal entry tagged work saying I finished the project."
                                      }
                         , Category = "journal")]
    public string AddJournalEntry ([NaturalLanguageParam(Description = "The text of the journal entry.")] string text
                                 , [NaturalLanguageParam(Description = "Optional tags for the entry (comma-separated)."
                                                       , Optional = true
                                                       , DefaultValue = ""
                                   )]
                                   string? tags
                                 , [NaturalLanguageParam(Description = "Optional context or category."
                                                       , Optional = true
                                                       , DefaultValue = ""
                                   )]
                                   string? context)
    {
        var tagList = string.IsNullOrWhiteSpace(tags)
                              ? null
                              : tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(t => t.Trim())
                                    .Where(t => t.Length > 0)
                                    .ToList();

        var id = _journal.AddEntry(text
                                 , tagList
                                 , string.IsNullOrWhiteSpace(context)
                                           ? null
                                           : context.Trim());

        return $"Journal entry added with ID '{id}'.";
    }

    // ----------------------------------------------------------------------
    // 2. ListJournalEntries
    // ----------------------------------------------------------------------
    [NaturalLanguageAction(Description = "Lists journal entries, optionally filtered by date."
                         , Examples = new[]
                                      {
                                              "Show my journal entries."
                                            , "List my entries from last week."
                                            , "What did I write today?"
                                            , "Show journal entries from January 1st to January 5th."
                                      }
                         , Category = "journal")]
    public string ListJournalEntries ([NaturalLanguageParam(Description = "Start date (optional)."
                                                          , Optional = true
                                                          , DefaultValue = ""
                                      )]
                                      string? fromDate
                                    , [NaturalLanguageParam(Description = "End date (optional)."
                                                          , Optional = true
                                                          , DefaultValue = ""
                                      )]
                                      string? toDate)
    {
        DateTimeOffset? fromUtc = ParseDate(fromDate);
        DateTimeOffset? toUtc   = ParseDate(toDate);

        var entries = _journal.ListEntries(fromUtc
                                         , toUtc);

        if (entries.Count == 0)
            return "No journal entries found.";

        var lines =
                entries.Select(entry => $"[{entry.CreatedUtc:yyyy-MM-dd HH:mm}] {entry.Text}{FormatTags(entry.Tags)}{FormatContext(entry.Context)} (ID: {entry.Id})");

        return string.Join("\n", lines);
    }

    // ----------------------------------------------------------------------
    // 3. GetJournalEntry
    // ----------------------------------------------------------------------
    [NaturalLanguageAction(Description = "Retrieves a single journal entry by ID."
                         , Examples = new[]
                                      {
                                              "Show the journal entry with ID abc123."
                                            , "Get journal entry 42."
                                            , "Read journal entry with ID 77."
                                      }
                         , Category = "journal")]
    public string GetJournalEntry ([NaturalLanguageParam(Description = "The ID of the journal entry.")] 
                                   string id)
    {
        var entry = _journal.GetEntry(id);

        if (entry is null) return $"No journal entry found with ID '{id}'.";

        return $"[{entry.CreatedUtc:yyyy-MM-dd HH:mm}] {entry.Text}"
             + $"{FormatTags(entry.Tags)}"
             + $"{FormatContext(entry.Context)}";
    }

    // ----------------------------------------------------------------------
    // 4. DeleteJournalEntry
    // ----------------------------------------------------------------------
    [NaturalLanguageAction(Description = "Deletes a journal entry by ID."
                         , Examples = new[]
                                      {
                                              "Delete journal entry abc123."
                                            , "Remove the entry with ID 42."
                                            , "Delete my last journal entry." // The model will extract the ID anyway
                                      }
                         , Category = "journal")]
    public string DeleteJournalEntry ([NaturalLanguageParam(Description = "The ID of the entry to delete.")] 
                                      string id)
    {
        var existing = _journal.GetEntry(id);

        if (existing is null) return $"No journal entry found with ID '{id}'.";

        _journal.DeleteEntry(id);

        return $"Journal entry '{id}' deleted.";
    }

    [NaturalLanguageAction(Description = "Lists all journal entries that occurred on this calendar day across all years."
                         , Examples = new[]
                                      {
                                              "What did I write on this day in history?"
                                            , "Show me my entries from this day over the years."
                                            , "Journal entries from this date in past years."
                                            , "What have I written on December 13 throughout the years?"
                                      }
                         , Category = "journal"
    )]
    public string JournalEntriesOnThisDay()
    {
        var today = DateTimeOffset.UtcNow;

        var entries = _journal.ListEntriesOnThisDay(today.Month
                                                  , today.Day);

        if (entries.Count == 0) return "You have no entries from this day in your personal history.";

        var lines = entries.Select(entry => $"[{entry.CreatedUtc:yyyy-MM-dd HH:mm}] "
                                          + $"{entry.Text}{FormatTags(entry.Tags)}{FormatContext(entry.Context)} "
                                          + $"(ID: {entry.Id})");

        return string.Join("\n", lines);
    }

    // ----------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------
    private static DateTimeOffset? ParseDate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        if (DateTimeOffset.TryParse(input, out var dateTimeOffset))
            return dateTimeOffset;

        return null;
    }

    private static string FormatTags(List<string>? tags) => tags is
                                                            {
                                                                    Count: > 0
                                                            } 
                                                                    ? $" [tags: {string.Join(", ", tags)}]" 
                                                                    : "";

    private static string FormatContext(string? context) => string.IsNullOrWhiteSpace(context) 
                                                                    ? "" 
                                                                    : $" [context: {context}]";
}
