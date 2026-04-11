using System.ComponentModel;
using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Avails.Extensions;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Domains.Journal;

[Category("journal")]
public sealed class JournalActions
{
    private readonly IJournalService       _journal;
    private readonly IJournalCommandParser _parser;
    private readonly ILlmClient            _llmClient;

    public JournalActions(IJournalService journal, IJournalCommandParser parser, ILlmClient llmClient)
    {
        _journal   = journal    ?? throw new ArgumentNullException(nameof(journal));
        _parser    = parser     ?? throw new ArgumentNullException(nameof(parser));
        _llmClient = llmClient  ?? throw new ArgumentNullException(nameof(llmClient));
    }

    // ----------------------------------------------------------------------
    // AddJournalEntry
    // ----------------------------------------------------------------------
    [FastPath]
    [NaturalLanguageAction(    Description = "Adds a new journal entry with optional tags, context, mood, and media."
                               , Examples = new[]
                                            {
                                                    "Write in my journal that I felt anxious but hopeful today."
                                                    , "Add a journal entry saying I had a productive day, mood positive."
                                                    , "Record that I exercised this morning and felt great."
                                                    , "Write a journal entry tagged work saying I finished the project."
                                                    , "Add a journal entry with mood score 2 saying I felt pretty low."
                                            }
                               , Category = "journal")]
    public string AddJournalEntry ([NaturalLanguageParam(    Description = "The text of the journal entry."
                                                             , AllowEmpty = false)]
                                   string text
                                   , [NaturalLanguageParam(    Description = "Optional tags for the entry (comma-separated)."
                                                               , Optional = true
                                                               , DefaultValue = ""
                                   )]
                                   string? tags
                                   
                                   , [NaturalLanguageParam(    Description = "Optional context or category."
                                                               , Optional = true
                                                               , DefaultValue = ""
                                   )]
                                   string? context
                                   
                                   , [NaturalLanguageParam(    Description = "Optional free-form mood description (for example: 'anxious but hopeful')."
                                                               , Optional = true)]
                                   string? mood = null
                                   
                                   , [NaturalLanguageParam(    Description = "Optional mood score from 1 (very negative) to 5 (very positive)."
                                                               , Optional = true)]
                                   string? moodScore = null
                                   
                                   , [NaturalLanguageParam(    Description = "Optional media file names or paths related to this entry (comma-separated)."
                                                               , Optional = true)]
                                   string? media = null)
    {
        // Parse the text through the command parser so block grammar
        // (Tags: / Mood: / MoodScore: directives) is handled at the ingestion boundary.
        // Fall back to explicit parameters when the parser finds nothing (LLM path).
        var parsed     = _parser.Parse(text);
        var finalText  = parsed.Text;
        var tagList    = parsed.Tags.Count > 0
                                 ? parsed.Tags.ToList()
                                 : SplitCommaSeparated(tags);
        var finalMood  = parsed.Mood
                      ?? (mood is not null ? mood.Replace(@"""", "") : null);
        var finalScore = parsed.MoodScore ?? TryParseMoodScore(moodScore);
        var mediaList  = SplitCommaSeparated(media);

        var id = _journal.AddEntryAsync(text: finalText
                                      , tags: tagList
                                      , mood: finalMood
                                      , moodScore: finalScore
                                      , moodLevel: -1
                                      , mediaPaths: mediaList);

        var shortenTextBy = finalText.Length < 25
                                    ? finalText.Length
                                    : 25;
        return $"Journal entry added: '{finalText[..shortenTextBy]}...'";
    }

    // ----------------------------------------------------------------------
    // 2. ListJournalEntries
    // ----------------------------------------------------------------------
    [FastPath]
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
        var entryWithRevisions = _journal.ListEntries(ParseDate(fromDate), ParseDate(toDate));

        if (entryWithRevisions.Count == 0)
            return "No journal entries found.";

        var sb = new StringBuilder();
        foreach (var entryWithRevision in entryWithRevisions)
        {
            sb.Append('[')
              .Append(entryWithRevision.Entry.CreatedUtc.ToString("yyyy-MM-dd HH:mm"))
              .Append("] ")
              .Append(entryWithRevision.LatestRevision.Text);

            AppendCommonMetadata(sb, entryWithRevision, includeId: true);
            sb.AppendLine().AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    // ----------------------------------------------------------------------
    // 3. GetJournalEntry
    // ----------------------------------------------------------------------
    [FastPath]
    [NaturalLanguageAction(Description = "Retrieves a single journal entry by ID."
                         , Examples = new[]
                                      {
                                              "Show the journal entry with ID abc123."
                                            , "Get journal entry 42."
                                            , "Read journal entry with ID 77."
                                      }
                         , Category = "journal")]
    public string GetJournalEntry ([NaturalLanguageParam(Description = "The ID of the journal entry."
                                   , AllowEmpty = false)] 
                                   string id)
    {
        var entryWithRevision = _journal.GetById(id); //.GetEntry(id);
        
        var sb = new StringBuilder();
        sb.Append('[')
          .Append(entryWithRevision.Entry.CreatedUtc.ToString("yyyy-MM-dd HH:mm"))
          .Append("] ")
          .Append(entryWithRevision.LatestRevision.Text);

        AppendCommonMetadata(sb, entryWithRevision, includeId: false);

        return sb.ToString();
    }

    // ----------------------------------------------------------------------
    // 4. DeleteJournalEntry
    // ----------------------------------------------------------------------
    [FastPath]
    [DestructiveAction]
    [NaturalLanguageAction(Description = "Deletes a journal entry by ID. And a reason must be provided"
                         , Examples = new[]
                                      {
                                              "Delete journal entry abc123, because it was added by mistake."
                                            , "Remove the entry with ID 42. It is no long needed"
                                            , "Delete my last journal entry. It is not complete" // The model will extract the ID anyway
                                      }
                         , Category = "journal")]
    public string DeleteJournalEntry ([NaturalLanguageParam(Description = "The ID of the entry to delete."
                                                          , AllowEmpty = false)]
                                      string id
                                    , [NaturalLanguageParam(Description = "Reason Entry was deleted"
                                                          , AllowEmpty = false)]
                                      string reason)
    {
        var deleted = _journal.DeleteEntry(id, reason);
        return deleted
                       ? $"Journal entry '{id}' deleted."
                       : $"No journal entry found with ID '{id}'. Or a reason must be provided.";
    }

    [FastPath]
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

        var entryWithRevisions = _journal.ListEntries()
                              .Where(entryWithRevision => entryWithRevision.Entry.CreatedUtc.Month == today.Month 
                                           && entryWithRevision.Entry.CreatedUtc.Day   == today.Day 
                                           && entryWithRevision.Entry.CreatedUtc.Year != today.Year)
                              .OrderBy(entryWithRevision => entryWithRevision.Entry.CreatedUtc)
                              .ToList();

        if (entryWithRevisions.Count == 0)
            return "You have no entries from this day in your personal history.";

        var sb = new StringBuilder();
        sb.AppendLine("Entries from this day in your personal history:");
        
        foreach (var entryWithRevision in entryWithRevisions)
        {
            sb.Append('[')
              .Append(entryWithRevision.Entry.CreatedUtc.ToString("yyyy-MM-dd HH:mm"))
              .Append("] ")
              .Append(entryWithRevision.LatestRevision.Text);

            AppendCommonMetadata(sb, entryWithRevision, includeId: true);
            sb.AppendLine().AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    // ----------------------------------------------------------------------
    // 5. SearchJournalEntries
    // ----------------------------------------------------------------------
    [FastPath]
    [NaturalLanguageAction(Description = "Searches journal entries for a keyword, optionally within a date range. Matches against entry text, tags, and mood."
                         , Examples = new[]
                                      {
                                              "Search my journal for 'Jake'."
                                            , "Find journal entries mentioning work."
                                            , "Search for entries tagged 'focus' last month."
                                            , "Did I write anything about the project deadline?"
                                      }
                         , Category = "journal")]
    public string SearchJournalEntries ([NaturalLanguageParam(Description = "The keyword or phrase to search for."
                                                            , AllowEmpty = false)]
                                        string keyword
                                      , [NaturalLanguageParam(Description = "Start date (optional)."
                                                            , Optional = true
                                                            , DefaultValue = "")]
                                        string? fromDate = null
                                      , [NaturalLanguageParam(Description = "End date (optional)."
                                                            , Optional = true
                                                            , DefaultValue = "")]
                                        string? toDate = null)
    {
        var results = _journal.SearchEntries(keyword
                                           , ParseDate(fromDate)
                                           , ParseDate(toDate));

        if (results.Count == 0)
            return $"No journal entries found containing '{keyword}'.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {results.Count} journal {(results.Count == 1 ? "entry" : "entries")} containing '{keyword}':");
        sb.AppendLine();

        foreach (var ewr in results)
        {
            sb.Append('[')
              .Append(ewr.Entry.CreatedUtc.ToString("yyyy-MM-dd HH:mm"))
              .Append("] ")
              .Append(ewr.LatestRevision.Text);

            AppendCommonMetadata(sb, ewr, includeId: true);
            sb.AppendLine().AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    // ----------------------------------------------------------------------
    // 6. AnalyzeJournal
    // ----------------------------------------------------------------------
    [NaturalLanguageAction(Description = "Answers a question about your journal by reading your entries and reasoning over them using AI. Use this for questions that require understanding meaning, not just finding a keyword."
                         , Examples = new[]
                                      {
                                              "What was I frustrated about last week?"
                                            , "How has my mood trended this month?"
                                            , "What themes keep coming up in my journal?"
                                            , "Summarize what I've been working on this week."
                                            , "What patterns do you notice in my entries?"
                                      }
                         , Category = "journal")]
    public async Task<string> AnalyzeJournal ([NaturalLanguageParam(Description = "The question or analysis request about your journal entries."
                                                                   , AllowEmpty = false)]
                                              string question
                                            , [NaturalLanguageParam(Description = "Start date — limit analysis to entries from this date onwards (optional)."
                                                                   , Optional = true
                                                                   , DefaultValue = "")]
                                              string? fromDate = null
                                            , [NaturalLanguageParam(Description = "End date — limit analysis to entries up to this date (optional)."
                                                                   , Optional = true
                                                                   , DefaultValue = "")]
                                              string? toDate = null)
    {
        var entries = _journal.ListEntries(ParseDate(fromDate), ParseDate(toDate));

        if (entries.Count == 0)
            return "No journal entries found for the specified date range.";

        var sb = new StringBuilder();
        sb.AppendLine("The following are journal entries belonging to the user. Answer the user's question based only on what is written here. Be honest if the entries do not contain enough information to answer well.");
        sb.AppendLine();

        foreach (var ewr in entries)
        {
            sb.Append('[').Append(ewr.Entry.CreatedUtc.ToString("yyyy-MM-dd")).Append("] ");
            sb.Append(ewr.LatestRevision.Text);

            if (ewr.LatestRevision.Mood is not null)
                sb.Append($" [mood: {ewr.LatestRevision.Mood}]");

            if (ewr.LatestRevision.MoodScore.HasValue)
                sb.Append($" [mood score: {ewr.LatestRevision.MoodScore}]");

            if (ewr.LatestRevision.Tags is { Count: > 0 })
                sb.Append($" [tags: {string.Join(", ", ewr.LatestRevision.Tags)}]");

            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine($"User's question: {question}");

        var prompt = sb.ToString();

        return await _llmClient.SendAsync(prompt);
    }

    // ----------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------
    private static List<string> SplitCommaSeparated(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new List<string>();

        value = value.Replace(@""""
                            , "");
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0)
                    .ToList();
    }
    
    private static int? TryParseMoodScore(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (int.TryParse(value, out var score)
               .Not()) return null;

        if (score is < 1 or > 5) return null;

        return score;
    }

    private static void AppendCommonMetadata (StringBuilder  sb
                                              , JournalEntryWithRevision entryWithRevision
                                              , bool         includeId)
    {
        if (entryWithRevision.LatestRevision.Tags is { Count: > 0 })
        {
            sb.Append(" [tags: ")
              .Append(string.Join(", "
                                  , entryWithRevision.LatestRevision.Tags))
              .Append(']');
        }
        
        if (entryWithRevision.LatestRevision.Mood.HasValue()
         || entryWithRevision.LatestRevision.MoodScore.HasValue
         || entryWithRevision.LatestRevision.MoodLevel.HasValue)
        {
            sb.Append(" [mood: ");

            var hadText = false;

            if (entryWithRevision.LatestRevision.Mood.HasValue())
            {
                sb.Append(entryWithRevision.LatestRevision.Mood);
                hadText = true;
            }

            if (entryWithRevision.LatestRevision.MoodScore.HasValue)
            {
                if (hadText) sb.Append("; ");

                sb.Append("score ")
                  .Append(entryWithRevision.LatestRevision.MoodScore.Value);

                if (entryWithRevision.LatestRevision.MoodLevel.HasValue)
                {
                    sb.Append(" (")
                      .Append(entryWithRevision.LatestRevision.MoodLevel.Value)
                      .Append(')');
                }
            }
            else if (entryWithRevision.LatestRevision.MoodLevel.HasValue)
            {
                if (hadText) sb.Append("; ");

                sb.Append(entryWithRevision.LatestRevision.MoodLevel.Value);
            }

            sb.Append(']');
        }

        if (entryWithRevision.LatestRevision.MediaPaths is { Count: > 0 })
        {
            sb.Append(" [media: ")
              .Append(string.Join(", "
                                  , entryWithRevision.LatestRevision.MediaPaths))
              .Append(']');
        }

        if (includeId)
        {
            sb.Append(" (ID: ")
              .Append(entryWithRevision.Entry.Id)
              .Append(')');
        }
    }

    private static DateTimeOffset? ParseDate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        if (DateTimeOffset.TryParse(input, out var dateTimeOffset))
            return dateTimeOffset;

        return null;
    }
}
