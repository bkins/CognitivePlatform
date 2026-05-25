using CognitivePlatform.Api.Execution;

namespace CognitivePlatform.Api.Domains.System;

public sealed class PlatformInfoHandler : IActionHandler
{
    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
    {
        var sysService  = context.Require<ISystemInfoService>();
        var runtimeInfo = sysService.GetRuntimeInfo();

        var env = runtimeInfo.Environment;
        var ver = runtimeInfo.Version;

        var message = $"Platform: {ver.Application} {ver.Version} ({ver.BuildConfiguration}) | "
                    + $"Environment: {env.EnvironmentName} | Machine: {env.MachineName}";

        return Task.FromResult(new ActionResult
                               {
                                       Success = true
                                     , Message = message
                               });
    }
}
