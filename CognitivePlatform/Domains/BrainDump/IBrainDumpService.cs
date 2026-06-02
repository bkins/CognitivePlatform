namespace CognitivePlatform.Api.Domains.BrainDump;

public interface IBrainDumpService
{
    BrainDumpSession StartSession();

    BrainDumpSession? GetSession(string id);

    IReadOnlyList<BrainDumpSession> ListSessions(int count = 10);

    BrainDumpSession? UpdateCategory(string id, BrainDumpCategory category, string text);

    BrainDumpSession? MarkProcessed( string                  id
                                   , string?                 extractionSummary
                                   , IReadOnlyList<string>   taskIds
                                   , IReadOnlyList<string>   insightIds );

    void Delete(string id);
}
