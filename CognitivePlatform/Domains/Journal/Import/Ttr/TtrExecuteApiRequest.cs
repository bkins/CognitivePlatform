namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed class TtrExecuteApiRequest
{
    public TtrImportPlanRequest      Source    { get; init; } = new();
    public TtrImportExecutionRequest Execution { get; init; } = new();
}
