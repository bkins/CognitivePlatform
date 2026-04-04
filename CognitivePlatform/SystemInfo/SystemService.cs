using System.Reflection;
using CognitivePlatform.Api.Models.SystemInfo;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.SystemInfo;

public sealed class SystemService
{
    private static readonly DateTime _startedAtUtc = DateTime.UtcNow;

    private readonly IHostEnvironment _hostEnvironment;
    private readonly string           _dataRoot;
    private readonly string           _databasePath;

    public SystemService (IHostEnvironment hostEnvironment
                        , string           dataRoot
                        , string           databasePath)
    {
        _hostEnvironment = hostEnvironment;
        _dataRoot        = dataRoot;
        _databasePath    = databasePath;
    }

    public SystemEnvironmentInfo GetEnvironment()
    {
        return new SystemEnvironmentInfo
               {
                       EnvironmentName = _hostEnvironment.EnvironmentName
                     , MachineName     = Environment.MachineName
                     , ContentRoot     = _hostEnvironment.ContentRootPath
                     , DataRoot        = _dataRoot
                     , DatabasePath    = _databasePath
                     , ProcessId       = Environment.ProcessId
                     , StartedAtUtc    = _startedAtUtc
               };
    }

    public SystemVersionInfo GetVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                           ?.InformationalVersion;

        return new SystemVersionInfo
               {
                       Application          = assembly.GetName().Name ?? "Unknown"
                     , Version              = assembly.GetName().Version?.ToString()
                     , InformationalVersion = informationalVersion
                     , CommitSha            = ExtractCommitSha(informationalVersion)
                     , BuildConfiguration   = GetBuildConfiguration()
                     , BuildTimeUtc         = GetBuildTimeUtc(assembly)
               };
    }

    private static string GetBuildConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static string? ExtractCommitSha (string? informationalVersion)
    {
        // Example: "0.9.0+3b76b920"
        if (informationalVersion?.HasNoValue() ?? true)
            return null;

        var plusIndex = informationalVersion.IndexOf('+');
        
        return plusIndex > -1 
            && plusIndex < informationalVersion.Length - 1
                       ? informationalVersion[(plusIndex + 1)..]
                       : null;
    }

    private static DateTime GetBuildTimeUtc (Assembly assembly)
    {
        // Best-effort only; returns null if not available
        var location = assembly.Location;
        
        if (location.HasNoValue() 
         || File.Exists(location)
                .Not())
            return default;

        return File.GetLastWriteTimeUtc(location);
    }
}
