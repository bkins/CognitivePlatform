namespace CognitivePlatform.Api.Domains.Journal;

public interface IJournalDraftRepository
{
    Task AddAsync(JournalDraft draft, CancellationToken ct = default);
}