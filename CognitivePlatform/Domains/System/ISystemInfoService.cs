using CognitivePlatform.Api.Models.SystemInfo;

namespace CognitivePlatform.Api.Domains.System;

public interface ISystemInfoService
{
    SystemVersionInfo GetVersion();

    SystemEnvironmentInfo GetEnvironment();

    string GetBuildConfiguration();

    RuntimeInfo GetRuntimeInfo();

    string? GetConfigurationValue(string key);
}