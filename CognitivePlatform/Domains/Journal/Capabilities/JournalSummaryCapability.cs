using System.Text;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Registry.Capabilities;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Domains.Journal.Capabilities;

public sealed class JournalSummaryCapability : ICapabilityDefinition<object>
{
    public IDomainDefinition Domain        => new JournalDomain();
    public string            PromptSummary => "Summarize recent journal entries over a time window.";

    public IReadOnlyList<ActionMetadata> BuildActionDefinitions()
    {
        return new List<ActionMetadata>
               {
                   new ActionDefinitionBuilder()
                       .Named("SummarizeRecentJournalEntries")
                       .WithDescription("Summarizes journal entries from the past N days.")
                       .InDomain(new JournalDomain())
                       .WithExamples("Summarize my last week of journal entries"
                                   , "What have I been writing about recently?")
                       .WithParameter("days", typeof(int), "Number of days to look back", isOptional: true, defaultValue: 7)
                       .HandledBy(new JournalSummaryHandler())
                       .Build()
               }.AsReadOnly();
    }
}

public sealed class JournalSummaryHandler : IActionHandler
{
    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
    {
        var journalService = context.Require<IJournalService>();

        var daysStr = context.Parameters.TryGetValue("days", out var rawDays) ? rawDays : "7";
        var days    = int.TryParse(daysStr, out var parsedDays) ? parsedDays : 7;

        var fromUtc = DateTimeOffset.UtcNow.AddDays(-days);
        var entries = journalService.ListEntries(fromUtc: fromUtc);

        if (entries.Count == 0)
            return Task.FromResult(new ActionResult
                                   {
                                           Success = true
                                         , Message = $"No journal entries found in the past {days} days."
                                   });

        var summary = new StringBuilder();
        summary.AppendLine($"Found {entries.Count} journal entries from the past {days} days:");

        foreach (var entryWithRevision in entries.Take(10))
        {
            var revision = entryWithRevision.LatestRevision;
            var preview  = revision.Text.Length > 100
                               ? revision.Text[..100] + "..."
                               : revision.Text;
            summary.AppendLine($"- {revision.CreatedUtc:yyyy-MM-dd}: {preview}");
        }

        return Task.FromResult(new ActionResult
                               {
                                       Success = true
                                     , Message = summary.ToString().TrimEnd()
                               });
    }
}
