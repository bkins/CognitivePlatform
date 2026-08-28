using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Registry.Domains;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Conversations;

[Domain(typeof(ConversationRecorderDomain))]
public sealed class ConversationActions
{
    private readonly IConversationService _conversationService;

    public ConversationActions( IConversationService conversationService )
    {
        _conversationService = conversationService;
    }

    [NaturalLanguageAction(
        Description = "Queries cognitive memories, facts, commitments, and decisions extracted from recorded conversations."
      , Examples = new[]
        {
            "What did Sarah tell me about her new job?"
          , "When did Parker and I discuss the local AI computer?"
          , "What decisions did I make in recent conversations?"
          , "What action items came out of our meetings?"
          , "What did we agree on regarding SQLite?"
        }
      , Category = "conversations"
    )]
    public async Task<string> QueryConversationMemory(
        [NaturalLanguageParam(Description = "The question, topic, or person to query from conversation memory.")]
        string query)
    {
        if (query.HasNoValue())
        {
            return "Please provide a question or topic to search across conversation memories.";
        }

        var candidateMemories = await _conversationService.QueryMemoriesAsync(query);

        if (candidateMemories.Count > 0)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Found {candidateMemories.Count} memory reference(s) from conversations:");
            builder.AppendLine();

            foreach (var memory in candidateMemories.Take(8))
            {
                var speakerTag = memory.Speaker.HasValue() ? $" (from {memory.Speaker})" : string.Empty;
                builder.AppendLine($"- **[{memory.Category}]**{speakerTag}: {memory.Content}");
            }

            return builder.ToString().TrimEnd();
        }

        // Fallback: search conversations directly by title/transcript
        var matchingConversations = await _conversationService.SearchConversationsAsync(query: query);
        if (matchingConversations.Count > 0)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Found {matchingConversations.Count} related conversation(s):");
            builder.AppendLine();

            foreach (var record in matchingConversations.Take(5))
            {
                var analysis = await _conversationService.GetAnalysisAsync(record.Id);
                var summaryText = analysis != null && analysis.Summary.HasValue()
                    ? $"\n  *Summary*: {analysis.Summary}"
                    : string.Empty;

                builder.AppendLine($"- **{record.Title}** ({record.RecordedAtUtc:yyyy-MM-dd HH:mm} UTC){summaryText}");
            }

            return builder.ToString().TrimEnd();
        }

        return $"No conversation memories or discussions found matching '{query}'.";
    }

    [NaturalLanguageAction(
        Description = "Lists recently recorded conversations with their summaries and status."
      , Examples = new[]
        {
            "list my conversations"
          , "show recent conversation recordings"
          , "what meetings have I recorded?"
        }
      , Category = "conversations"
    )]
    public async Task<string> ListRecentConversations(
        [NaturalLanguageParam(Description = "Maximum number of conversations to return.", Optional = true)]
        int limit = 5)
    {
        var records = await _conversationService.ListRecordingsAsync();
        if (records.Count == 0)
        {
            return "No recorded conversations found.";
        }

        var effectiveLimit = Math.Clamp(limit, 1, 20);
        var recent = records.OrderByDescending(record => record.RecordedAtUtc).Take(effectiveLimit).ToList();

        var builder = new StringBuilder();
        builder.AppendLine($"Recent conversations ({recent.Count} of {records.Count}):");
        builder.AppendLine();

        foreach (var record in recent)
        {
            var analysis = await _conversationService.GetAnalysisAsync(record.Id);
            var statusTag = analysis != null ? $"[{analysis.Status}]" : "[Recorded]";
            builder.AppendLine($"- **{record.Title}** — {record.RecordedAtUtc:yyyy-MM-dd HH:mm} UTC {statusTag}");

            if (analysis != null && analysis.Summary.HasValue())
            {
                builder.AppendLine($"  {analysis.Summary}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    [NaturalLanguageAction(
        Description = "Retrieves the full structured summary and key takeaways for a specific conversation."
      , Examples = new[]
        {
            "get conversation summary for Architecture Review"
          , "show summary of last conversation"
          , "what were the decisions in our sync?"
        }
      , Category = "conversations"
    )]
    public async Task<string> GetConversationSummary(
        [NaturalLanguageParam(Description = "Title, search term, or ID of the conversation.")]
        string queryOrId)
    {
        if (queryOrId.HasNoValue())
        {
            return "Please specify a conversation title or ID.";
        }

        ConversationRecord? target = null;
        if (Guid.TryParse(queryOrId, out var id))
        {
            target = await _conversationService.GetRecordingAsync(id);
        }

        if (target == null)
        {
            var matches = await _conversationService.SearchConversationsAsync(query: queryOrId);
            target = matches.FirstOrDefault();
        }

        if (target == null)
        {
            return $"Could not find a conversation matching '{queryOrId}'.";
        }

        var details = await _conversationService.GetConversationDetailsAsync(target.Id);
        if (details == null)
        {
            return $"Conversation '{target.Title}' not found.";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"# {target.Title}");
        builder.AppendLine($"*Recorded {target.RecordedAtUtc:yyyy-MM-dd HH:mm} UTC*");
        builder.AppendLine();

        if (details.Participants.Count > 0)
        {
            var participantList = string.Join(", ", details.Participants.Select(participant => participant.DisplayName ?? participant.SpeakerLabel));
            builder.AppendLine($"**Participants:** {participantList}");
            builder.AppendLine();
        }

        if (details.Analysis != null && details.Analysis.Summary.HasValue())
        {
            builder.AppendLine($"### Summary");
            builder.AppendLine(details.Analysis.Summary);
            builder.AppendLine();

            if (details.Analysis.Decisions.Count > 0)
            {
                builder.AppendLine("### Decisions");
                foreach (var decision in details.Analysis.Decisions)
                {
                    builder.AppendLine($"- {decision.Content}");
                }
                builder.AppendLine();
            }

            if (details.Analysis.ActionItems.Count > 0)
            {
                builder.AppendLine("### Action Items");
                foreach (var action in details.Analysis.ActionItems)
                {
                    builder.AppendLine($"- {action.Content}");
                }
                builder.AppendLine();
            }
        }
        else
        {
            builder.AppendLine("*(Analysis has not been run for this conversation yet. Run analyze to produce structured takeaways.)*");
        }

        return builder.ToString().TrimEnd();
    }

    [NaturalLanguageAction(
        Description = "Extracts cognitive memory candidates from a recorded conversation."
      , Examples = new[]
        {
            "extract memories from conversation"
          , "extract conversation memories"
        }
      , Category = "conversations"
    )]
    public async Task<string> ExtractConversationMemories(
        [NaturalLanguageParam(Description = "The ID or title of the conversation.")]
        string conversationId)
    {
        if (conversationId.HasNoValue())
        {
            return "Please provide a conversation ID or title.";
        }

        Guid targetId;
        if (!Guid.TryParse(conversationId, out targetId))
        {
            var matches = await _conversationService.SearchConversationsAsync(query: conversationId);
            var first = matches.FirstOrDefault();
            if (first == null)
            {
                return $"Could not find a conversation matching '{conversationId}'.";
            }
            targetId = first.Id;
        }

        var memories = await _conversationService.ExtractMemoriesAsync(targetId);
        if (memories.Count == 0)
        {
            return "No memory candidates were extracted from this conversation.";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Extracted {memories.Count} memory candidate(s):");
        builder.AppendLine();

        foreach (var memory in memories)
        {
            builder.AppendLine($"- **[{memory.Category}]**: {memory.Content}");
        }

        return builder.ToString().TrimEnd();
    }
}
