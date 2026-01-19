namespace CognitivePlatform.Api.Domains.Journal.Interfaces;

public interface IJournalRevisionRepository
{
    IReadOnlyList<JournalRevision> GetRevisionsByEntryId(string entryId);
}