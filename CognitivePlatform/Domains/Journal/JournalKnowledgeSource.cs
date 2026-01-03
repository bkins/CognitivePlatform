using CognitivePlatform.Api.KnowledgeInbox;

namespace CognitivePlatform.Api.Domains.Journal;

public sealed class JournalKnowledgeSource : IKnowledgeSource
{
    private readonly IJournalService _journalService;

    public KnowledgeKind Kind => KnowledgeKind.Journal;

    public JournalKnowledgeSource (IJournalService journalService)
    {
        _journalService = journalService;
    }

    public IEnumerable<KnowledgeItemDto> GetKnowledgeItems (
        KnowledgeQuery    query
      , CancellationToken ct)
    {
        // TODO: introduce filtering
        var entries = _journalService.ListEntries();

        foreach (var entry in entries)
        {
            yield return new KnowledgeItemDto
                         {
                                 Id             = Guid.Parse(entry.Id)
                               , Kind           = KnowledgeKind.Journal
                               , Title          = DeriveTitle(entry)
                               , Summary        = DeriveSummary(entry)
                               , CreatedAt      = entry.CreatedUtc
                               , LastModifiedAt = entry.CreatedUtc
                               , Status         = KnowledgeStatus.Active
                               , Tags           = entry.Tags ?? Array.Empty<string>().ToList()
                               , Importance     = null
                               , Urgency = null
                         };
        }
    }

    private static string DeriveTitle (JournalEntry entry)
    {
        return entry.Text
                    .Length <= 60
                       ? entry.Text
                       : string.Concat(entry.Text.AsSpan(0, 57), "…");
    }

    private static string? DeriveSummary (JournalEntry entry)
    {
        return entry.Text
                    .Length <= 140
                       ? null
                       : string.Concat(entry.Text.AsSpan(0, 137), "…");
    }
}
