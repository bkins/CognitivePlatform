using System.Reflection;

namespace CognitivePlatform.Api.Execution;

public interface IServiceInvoker
{
    Task<ActionResult> InvokeAsync(MethodInfo method
                                 , object     service
                                 , object[]   args
                                 , bool       dryRun);
}
