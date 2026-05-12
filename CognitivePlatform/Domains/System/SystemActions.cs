// Domains/System/SystemActions.cs

using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Models.SystemInfo;

namespace CognitivePlatform.Api.Domains.System;

public class SystemActions
{
    private readonly ISystemInfoService _systemInfoService;

    public SystemActions(ISystemInfoService systemInfoService)
    {
        _systemInfoService = systemInfoService;
    }

    [FastPath]
    [NaturalLanguageAction(Description = "Gets the current application version."
                         , Examples =
                           [
                                   "what version are you running"
                                 , "show app version"
                                 , "current version"
                           ]
                         , Category = "System"
    )]
    public SystemVersionInfo GetVersion()
    {
        return _systemInfoService.GetVersion();
    }

    [FastPath]
    [NaturalLanguageAction(Description = "Gets the current hosting environment."
                         , Examples =
                           [
                                   "what environment are you running in"
                                 , "show environment"
                                 , "current environment"
                           ]
                         , Category = "System"
    )]
    public SystemEnvironmentInfo GetEnvironment()
    {
        return _systemInfoService.GetEnvironment();
    }

    [FastPath]
    [NaturalLanguageAction(Description = "Gets the current build configuration (e.g. Debug or Release)."
                         , Examples =
                           [
                                   "what build configuration are you running"
                                 , "show build configuration"
                                 , "current build configuration"
                           ]
                         , Category = "System"
    )]
    public string GetBuildConfiguration()
    {
        return _systemInfoService.GetBuildConfiguration();
    }
    
    [FastPath]
    [NaturalLanguageAction(Description = "Gets the current system uptime."
                         , Examples =
                           [
                                   "how long has the server been running"
                                 , "show uptime"
                                 , "server uptime"
                           ]
                         , Category = "System"
    )]
    public string GetUptime()
    {
        return _systemInfoService.GetRuntimeInfo().Uptime.ToString(@"dd\.hh\:mm\:ss");
    }

    [FastPath]
    [NaturalLanguageAction(Description = "Gets build and runtime information."
                         , Examples =
                           [
                                   "show build info"
                                 , "what build are you running"
                                 , "runtime info"
                           ]
                         , Category = "System"
    )]
    public RuntimeInfo GetSystemStatus()
    {
        return _systemInfoService.GetRuntimeInfo();
    }

    [FastPath]
    [NaturalLanguageAction(Description = "Gets a safe configuration value."
                         , Examples =
                           [
                                   "show configuration value"
                                 , "what model are you using"
                                 , "show app setting"
                           ]
                         , Category = "System"
                         , AllowsClarification = true
    )]
    public string? GetConfigurationValue( [NaturalLanguageParam(Description = "The configuration key to retrieve."
                                                              , AllowEmpty = false)]
                                          string key )
    {
        return _systemInfoService.GetConfigurationValue(key);
    }
}