namespace CognitivePlatform.Api.Execution;

public static class ActionResultUtility
{
    public static async Task<ActionResult?> UnwrapAsync(object? result)
    {
        switch (result)
        {
            case null:
            {
                return null;
            }
            case Task task:
            {
                await task;

                if (task.GetType().IsGenericType)
                {
                    // Use 'as' so VoidTaskResult (Task.CompletedTask's hidden type) returns null
                    // rather than throwing InvalidCastException.
                    var value = task.GetType()
                                    .GetProperty("Result")?
                                    .GetValue(task);

                    return value as ActionResult;
                }

                return null;
            }
            default:
            {
                return result as ActionResult;
            }
        }
    }
}
