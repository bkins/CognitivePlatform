using System.ComponentModel;
using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Media;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry.Domains;
using CognitivePlatform.Api.SystemPromptLogging;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Journal;

[Category("journal")]
[Domain(typeof(JournalDomain))]
public sealed class JournalActions
{
    private readonly IJournalService         _journal;
    private readonly IJournalCommandParser   _parser;
    private readonly ILlmClient              _llmClient;
    private readonly IPromptLogger           _promptLogger;
    private readonly IMediaAttachmentService _mediaService;

    public JournalActions( IJournalService         journal
                         , IJournalCommandParser   parser
                         , ILlmClient              llmClient
                         , IPromptLogger           promptLogger
                         , IMediaAttachmentService mediaService )
    {
        _journal      = journal      ?? throw new ArgumentNullException(nameof(journal));
        _parser       = parser       ?? throw new ArgumentNullException(nameof(parser));
        _llmClient    = llmClient    ?? throw new ArgumentNullException(nameof(llmClient));
        _promptLogger = promptLogger ?? throw new ArgumentNullException(nameof(promptLogger));
        _mediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));
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
                               , Category = "journal"
                             , IsReplayable = true)]
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
        var from = ParseDate(fromDate);
        var to   = ParseDate(toDate);

        var allOrdered = _journal.GetOrderedEntries();
        var filtered   = allOrdered
                         .Where(orderedEntry => (from is null || orderedEntry.EntryWithRevision.Entry.CreatedUtc >= from)
                                             && (to   is null || orderedEntry.EntryWithRevision.Entry.CreatedUtc <= to))
                         .ToList();

        if (filtered.Count == 0)
            return "No journal entries found.";

        var sb = new StringBuilder();

        foreach (var (position, entryWithRevision) in filtered)
        {
            sb.AppendLine($"## {position}. {entryWithRevision.Entry.CreatedUtc.ToLocalTime():yyyy-MM-dd}");
            sb.AppendLine();
            sb.Append(entryWithRevision.LatestRevision.Text);

            AppendCommonMetadata(sb, entryWithRevision);
            sb.AppendLine().AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    // ----------------------------------------------------------------------
    // 3. GetJournalEntry
    // ----------------------------------------------------------------------
    [FastPath]
    [NaturalLanguageAction(Description = "Retrieves a single journal entry by its 1-based position number. Ordinal words (first, second, third) map to position 1, 2, 3."
                         , Examples = new[]
                                      {
                                              "Show journal entry 3."
                                            , "Read entry 7."
                                            , "Get journal entry 12."
                                            , "Show me the third journal entry."
                                            , "Read the first entry."
                                            , "Get the second entry."
                                      }
                         , Category = "journal")]
    public string GetJournalEntry ([NaturalLanguageParam(Description = "The 1-based position number of the journal entry (e.g. '3'). Ordinal words map to integers: 'first'=1, 'second'=2, 'third'=3."
                                                       , AllowEmpty  = false)]
                                   string entryReference)
    {
        var entryWithRevision = TryResolveJournalReference(entryReference, out var errorMessage);

        if (entryWithRevision is null)
            return errorMessage!;

        var sb = new StringBuilder();
        sb.AppendLine($"## {entryReference}. {entryWithRevision.Entry.CreatedUtc.ToLocalTime():yyyy-MM-dd}");
        sb.AppendLine();
        sb.Append(entryWithRevision.LatestRevision.Text);

        AppendCommonMetadata(sb, entryWithRevision);

        return sb.ToString();
    }

    // ----------------------------------------------------------------------
    // 4. DeleteJournalEntry
    // ----------------------------------------------------------------------
    [FastPath]
    [DestructiveAction]
    [NaturalLanguageAction(Description = "Deletes a journal entry by its position number. A reason must be provided."
                         , Examples = new[]
                                      {
                                              "Delete journal entry 3, it was added by mistake."
                                            , "Remove entry 5 — it is no longer needed."
                                            , "Delete my last journal entry. It is not complete."
                                      }
                         , Category = "journal", IsReplayable = true)]
    public string DeleteJournalEntry ([NaturalLanguageParam(Description = "The position number of the entry to delete (e.g. '3') from a recent listing."
                                                          , AllowEmpty  = false)]
                                      string entryReference
                                    , [NaturalLanguageParam(Description = "Reason the entry is being deleted."
                                                          , AllowEmpty  = false)]
                                      string reason)
    {
        var entryWithRevision = TryResolveJournalReference(entryReference, out var errorMessage);

        if (entryWithRevision is null)
            return errorMessage!;

        var deleted = _journal.DeleteEntry(entryWithRevision.Entry.Id, reason);

        return deleted
                       ? $"Journal entry {entryReference} — '{entryWithRevision.LatestRevision.Text[..Math.Min(40, entryWithRevision.LatestRevision.Text.Length)]}...' deleted."
                       : $"Could not delete journal entry {entryReference}.";
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

        var filtered = _journal.GetOrderedEntries()
                               .Where(orderedEntry => orderedEntry.EntryWithRevision.Entry.CreatedUtc.Month == today.Month
                                                   && orderedEntry.EntryWithRevision.Entry.CreatedUtc.Day   == today.Day
                                                   && orderedEntry.EntryWithRevision.Entry.CreatedUtc.Year  != today.Year)
                               .ToList();

        if (filtered.Count == 0)
            return "You have no entries from this day in your personal history.";

        var sb = new StringBuilder();
        sb.AppendLine("Entries from this day in your personal history:");
        sb.AppendLine();

        foreach (var (position, entryWithRevision) in filtered)
        {
            sb.AppendLine($"## {position}. {entryWithRevision.Entry.CreatedUtc.ToLocalTime():yyyy-MM-dd}");
            sb.AppendLine();
            sb.Append(entryWithRevision.LatestRevision.Text);

            AppendCommonMetadata(sb, entryWithRevision);
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
        var matchingIds = _journal.SearchEntries(keyword
                                               , ParseDate(fromDate)
                                               , ParseDate(toDate))
                                 .Select(entryWithRevision => entryWithRevision.Entry.Id)
                                 .ToHashSet();

        var allOrdered = _journal.GetOrderedEntries();
        var results    = allOrdered.Where(orderedEntry => matchingIds.Contains(orderedEntry.EntryWithRevision.Entry.Id))
                                   .ToList();

        if (results.Count == 0)
            return $"No journal entries found containing '{keyword}'.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {results.Count} journal {(results.Count == 1 ? "entry" : "entries")} containing '{keyword}':");
        sb.AppendLine();

        foreach (var (position, entryWithRevision) in results)
        {
            sb.AppendLine($"## {position}. {entryWithRevision.Entry.CreatedUtc.ToLocalTime():yyyy-MM-dd}");
            sb.AppendLine();
            sb.Append(entryWithRevision.LatestRevision.Text);

            AppendCommonMetadata(sb, entryWithRevision);
            sb.AppendLine().AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    // ----------------------------------------------------------------------
    // 6. GetJournalHistory
    // ----------------------------------------------------------------------
    [FastPath]
    [NaturalLanguageAction(Description = "Shows the revision history of a journal entry — the original and all subsequent edits with their timestamps."
                         , Examples = new[]
                                      {
                                              "Show the history of journal entry 3."
                                            , "How many times has entry 5 been edited?"
                                            , "Show me the revision history for entry 2."
                                            , "What did entry 7 look like originally?"
                                      }
                         , Category = "journal")]
    public string GetJournalHistory ([NaturalLanguageParam(Description = "The 1-based position number of the journal entry (e.g. '3'). Ordinal words map to integers: 'first'=1, 'second'=2, 'third'=3."
                                                         , AllowEmpty  = false)]
                                     string entryReference)
    {
        var entryWithRevision = TryResolveJournalReference(entryReference, out var errorMessage);

        if (entryWithRevision is null)
            return errorMessage!;

        var revisions = _journal.GetRevisionHistory(entryWithRevision.Entry.Id);
        var count     = revisions.Count;

        if (count == 1)
        {
            var timestamp = revisions[0].CreatedUtc.ToLocalTime().ToString("MMM d 'at' h:mm tt");
            return $"Entry #{entryReference} has 1 revision — created {timestamp} (current).";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Entry #{entryReference} has {count} revisions:");

        for (var index = 0; index < revisions.Count; index++)
        {
            var revision  = revisions[index];
            var timestamp = revision.CreatedUtc.ToLocalTime().ToString("MMM d 'at' h:mm tt");
            var label     = index == 0 ? "original" : "updated";
            var current   = index == revisions.Count - 1 ? " (current)" : string.Empty;
            var excerpt   = revision.Text.Length <= 50
                                    ? revision.Text
                                    : revision.Text[..50] + "...";

            sb.AppendLine($"  {index + 1}. {label} — {timestamp}{current}: \"{excerpt}\"");
        }

        return sb.ToString().TrimEnd();
    }

    // ----------------------------------------------------------------------
    // 7. AnalyzeJournal
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
        //TODO: This action is doing a lot of work that could be moved to a service — fetching and formatting
        // the relevant entries, constructing the prompt, etc.
        // Refactor to move that work out of the action method and into a service that can be unit tested
        // without needing to mock ILlmClient. (see InsightsActions.AnalyzePatterns `TO_DO` for similar refactor)
        
        var entries = _journal.ListEntries(ParseDate(fromDate), ParseDate(toDate));

        if (entries.Count == 0)
            return "No journal entries found for the specified date range.";

        var sb = new StringBuilder();
        sb.AppendLine("The following are journal entries belonging to the user. Answer the user's question based only on what is written here. Be honest if the entries do not contain enough information to answer well.");
        sb.AppendLine();

        foreach (var entryWithRevision in entries)
        {
            sb.Append('[').Append(entryWithRevision.Entry.CreatedUtc.ToString("yyyy-MM-dd")).Append("] ");
            sb.Append(entryWithRevision.LatestRevision.Text);

            if (entryWithRevision.LatestRevision.Mood is not null)
                sb.Append($" [mood: {entryWithRevision.LatestRevision.Mood}]");

            if (entryWithRevision.LatestRevision.MoodScore.HasValue)
                sb.Append($" [mood score: {entryWithRevision.LatestRevision.MoodScore}]");

            if (entryWithRevision.LatestRevision.Tags is { Count: > 0 })
                sb.Append($" [tags: {string.Join(", ", entryWithRevision.LatestRevision.Tags)}]");

            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine($"User's question: {question}");

        var prompt = sb.ToString();
        _promptLogger.Log("AnalyzeJournal"
                        , prompt
                        , _llmClient.GetType().Name);
        
        return (await _llmClient.SendAsync(prompt)).Content;
    }

    // ----------------------------------------------------------------------
    // 8. GetJournalAttachments
    // ----------------------------------------------------------------------
    [NaturalLanguageAction(
        Description = "Lists all media attachments for a journal entry."
      , Examples    = new[]
                      {
                          "show attachments for journal entry 1"
                        , "what files are attached to journal entry abc123"
                        , "list media for entry 5"
                      }
      , Category    = "journal")]
    public async Task<string> GetJournalAttachments(
        [NaturalLanguageParam(Description = "The journal entry reference — a 1-based position number or entry ID.", AllowEmpty = false)]
        string entryReference)
    {
        var entryWithRevision = TryResolveJournalReference(entryReference, out var errorMessage);
        if (entryWithRevision is null)
            return errorMessage!;

        var attachments = await _mediaService.GetAttachmentsAsync("JournalEntry"
                                                                , entryWithRevision.Entry.Id);

        if (attachments.Count == 0)
            return $"No media attachments for journal entry {entryReference}.";

        var sb = new StringBuilder();
        sb.AppendLine($"Media attachments for entry {entryReference} ({attachments.Count}):");
        sb.AppendLine();

        foreach (var attachment in attachments)
        {
            var sizeKb = attachment.FileSizeBytes / 1024.0;
            sb.AppendLine($"• {attachment.FileName} ({attachment.ContentType}, {sizeKb:F1} KB)");
        }

        return sb.ToString().TrimEnd();
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

    private static void AppendCommonMetadata(StringBuilder sb, JournalEntryWithRevision entryWithRevision)
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

    }

    /// <summary>
    /// Resolves a journal reference that is either a 1-based position integer
    /// or a raw entry ID string. Returns the resolved entry, or null with an
    /// error message in <paramref name="errorMessage"/>.
    /// </summary>
    private JournalEntryWithRevision? TryResolveJournalReference( string      reference
                                                                 , out string? errorMessage )
    {
        reference = (reference ?? string.Empty).Trim();

        if (int.TryParse(reference, out var position))
        {
            var byPosition = _journal.ResolveByPosition(position);

            if (byPosition is null)
            {
                errorMessage = $"No journal entry found at position {position}.";
                return null;
            }

            errorMessage = null;
            return byPosition;
        }

        try
        {
            var byId = _journal.GetById(reference);
            errorMessage = null;
            return byId;
        }
        catch (KeyNotFoundException)
        {
            errorMessage = $"No journal entry found with id '{reference}'.";
            return null;
        }
    }

    private static DateTimeOffset? ParseDate(string? input)
    {
        if (input?.HasNoValue() ?? true) return null;
        if (DateTimeOffset.TryParse(input, out var dateTimeOffset)) return dateTimeOffset;

        return null;
    }
}
