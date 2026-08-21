using CognitivePlatform.Api.Execution;

namespace CognitivePlatform.Api.Registry.Capabilities;

public sealed class CrudAddHandler<TEntity> : IActionHandler where TEntity : class
{
    public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
    {
        try
        {
            var service = context.Require<ICrudService<TEntity>>();
            var entity  = await service.AddAsync(context.Parameters, context.CancellationToken);

            return new ActionResult
                   {
                       Success = true
                     , Message = service.FormatForDetail(entity)
                   };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ActionResult { Success = false, Message = ex.Message };
        }
    }
}
