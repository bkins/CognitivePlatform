// Domains/System/RuntimeInfo.cs

using CognitivePlatform.Api.Models.SystemInfo;

namespace CognitivePlatform.Api.Domains.System;

public class RuntimeInfo
{
    public SystemEnvironmentInfo Environment { get; init; } = new();

    public SystemVersionInfo Version { get; init; } = new();

    public TimeSpan Uptime { get; init; }

    public BuildInfo BuildInfo { get; init; } = new();
}