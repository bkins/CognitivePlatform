namespace CognitivePlatform.Admin.Services;

public interface IBacklogBoardCompiler
{
    Task CompileAsync(CancellationToken cancellationToken = default);
}
