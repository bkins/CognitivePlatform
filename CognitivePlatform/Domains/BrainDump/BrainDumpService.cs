using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Workspace;

namespace CognitivePlatform.Api.Domains.BrainDump;

public class BrainDumpService : IBrainDumpService
{
    private readonly IObjectStore         _store;
    private readonly IWorkspaceContext    _workspaceContext;
    private readonly ILogger<BrainDumpService> _logger;

    public BrainDumpService( IObjectStore              store
                           , IWorkspaceContext          workspaceContext
                           , ILogger<BrainDumpService>  logger )
    {
        _store            = store;
        _workspaceContext = workspaceContext;
        _logger           = logger;
    }

    public BrainDumpSession StartSession()
    {
        var session = new BrainDumpSession
                      {
                          Id        = Guid.NewGuid().ToString("N")
                        , CreatedAt = DateTimeOffset.UtcNow
                        , UpdatedAt = DateTimeOffset.UtcNow
                      };

        SaveInternal(session);
        _logger.LogInformation("BrainDump session started: {Id}", session.Id);
        return session;
    }

    public BrainDumpSession? GetSession(string id)
    {
        return _store.Get<BrainDumpSession>(id, _workspaceContext.ActivePartitionKey);
    }

    public IReadOnlyList<BrainDumpSession> ListSessions(int count = 10)
    {
        return _store.List<BrainDumpSession>(_workspaceContext.ActivePartitionKey)
                     .Where(session => !session.IsDeleted)
                     .OrderByDescending(session => session.CreatedAt)
                     .Take(count)
                     .ToList();
    }

    public BrainDumpSession? UpdateCategory(string id, BrainDumpCategory category, string text)
    {
        var session = GetSession(id);
        if (session is null)
            return null;

        switch (category)
        {
            case BrainDumpCategory.Avoidance:
                session.Avoidance        = text; break;
            case BrainDumpCategory.Fears:
                session.Fears            = text; break;
            case BrainDumpCategory.Frustrations:
                session.Frustrations     = text; break;
            case BrainDumpCategory.Discouragements:
                session.Discouragements  = text; break;
            case BrainDumpCategory.GoalsAndBarriers:
                session.GoalsAndBarriers = text; break;
            case BrainDumpCategory.HurtAndSorrow:
                session.HurtAndSorrow    = text; break;
            case BrainDumpCategory.SelfCriticism:
                session.SelfCriticism    = text; break;
        }

        session.UpdatedAt = DateTimeOffset.UtcNow;
        SaveInternal(session);
        return session;
    }

    public BrainDumpSession? MarkProcessed( string                id
                                          , string?               extractionSummary
                                          , IReadOnlyList<string> taskIds
                                          , IReadOnlyList<string> insightIds )
    {
        var session = GetSession(id);
        if (session is null)
            return null;

        session.Processed         = true;
        session.ProcessedAt       = DateTimeOffset.UtcNow;
        session.UpdatedAt         = DateTimeOffset.UtcNow;
        session.ExtractionSummary = extractionSummary;
        session.ExtractedTaskIds.AddRange(taskIds);
        session.ExtractedInsightIds.AddRange(insightIds);

        SaveInternal(session);
        _logger.LogInformation("BrainDump session {Id} marked processed.", session.Id);
        return session;
    }

    public void Delete(string id)
    {
        var session = GetSession(id);
        if (session is null)
            return;

        session.IsDeleted  = true;
        session.DeletedUtc = DateTimeOffset.UtcNow;
        session.UpdatedAt  = DateTimeOffset.UtcNow;
        SaveInternal(session);
    }

    private void SaveInternal(BrainDumpSession session)
    {
        _ = _store.Save(session, _workspaceContext.ActivePartitionKey, session.Id);
    }
}
