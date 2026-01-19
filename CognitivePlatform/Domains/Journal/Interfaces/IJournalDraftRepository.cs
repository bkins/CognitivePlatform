namespace CognitivePlatform.Api.Domains.Journal.Interfaces;

public interface IJournalDraftRepository
{
    Task AddAsync(JournalDraft draft, CancellationToken ct = default);
}