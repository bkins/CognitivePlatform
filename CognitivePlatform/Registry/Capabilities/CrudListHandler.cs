using CognitivePlatform.Api.Execution;

namespace CognitivePlatform.Api.Registry.Capabilities;

public sealed class CrudListHandler<TEntity> : IActionHandler where TEntity : class
{
    public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
    {
        try
        {
            var service  = context.Require<ICrudService<TEntity>>();
            var entities = await service.ListAsync(context.CancellationToken);

            if (entities.Count == 0)
            {
                return new ActionResult { Success = true, Message = "No entries found." };
            }

            var lines   = entities.Select(entity => service.FormatForList(entity));
            var message = string.Join(Environment.NewLine, lines);

            return new ActionResult { Success = true, Message = message };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ActionResult { Success = false, Message = ex.Message };
        }
    }
}
