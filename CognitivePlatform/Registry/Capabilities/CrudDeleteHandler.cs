using CognitivePlatform.Api.Execution;

namespace CognitivePlatform.Api.Registry.Capabilities;

public sealed class CrudDeleteHandler<TEntity> : IActionHandler where TEntity : class
{
    public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
    {
        try
        {
            var service = context.Require<ICrudService<TEntity>>();
            var id      = context.Parameters.TryGetValue("id", out var rawId) ? rawId : string.Empty;
            var deleted = await service.DeleteAsync(id, context.CancellationToken);

            return new ActionResult
                   {
                       Success = deleted
                     , Message = deleted
                                     ? $"Entry '{id}' deleted."
                                     : $"No entry found with ID '{id}'."
                   };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ActionResult { Success = false, Message = ex.Message };
        }
    }
}
