using CognitivePlatform.Api.Execution;

namespace CognitivePlatform.Api.Registry.Capabilities;

public sealed class CrudGetHandler<TEntity> : IActionHandler where TEntity : class
{
    public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
    {
        try
        {
            var service = context.Require<ICrudService<TEntity>>();
            var id      = context.Parameters.TryGetValue("id", out var rawId) ? rawId : string.Empty;
            var entity  = await service.GetAsync(id, context.CancellationToken);

            if (entity is null)
            {
                return new ActionResult { Success = false, Message = $"No entry found with ID '{id}'." };
            }

            return new ActionResult { Success = true, Message = service.FormatForDetail(entity) };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ActionResult { Success = false, Message = ex.Message };
        }
    }
}
