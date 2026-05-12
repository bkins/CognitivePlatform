using System.Reflection;
using System.Text;
using CognitivePlatform.Api.Models.SystemInfo;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Domains.System;

public class SystemService : ISystemInfoService
{
    private static readonly DateTime _startedAtUtc = DateTime.UtcNow;
    
    private readonly IConfiguration?    _configuration;
    private readonly IHostEnvironment   _hostEnvironment;
    private readonly SystemPathsOptions _paths;

    public SystemService( IHostEnvironment             hostEnvironment
                        , IOptions<SystemPathsOptions> pathsOptions
                        , IConfiguration               configuration = null )
    {
        _hostEnvironment = hostEnvironment;
        _paths           = pathsOptions.Value;
        _configuration   = configuration;
    }

    public SystemEnvironmentInfo GetEnvironment()
    {
        return new SystemEnvironmentInfo
               {
                       EnvironmentName = _hostEnvironment.EnvironmentName
                     , MachineName     = Environment.MachineName
                     , ContentRoot     = _hostEnvironment.ContentRootPath
                     , DataRoot        = _paths.DataRoot
                     , DatabasePath    = Path.Combine(_paths.DataRoot, _paths.DatabasePath)
                     , ProcessId       = Environment.ProcessId
                     , StartedAtUtc    = _startedAtUtc
               };
    }

    public TimeSpan GetUptime() => throw new NotImplementedException();

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

    public string GetBuildConfiguration()
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

    private static DateTime? GetBuildTimeUtc (Assembly assembly)
    {
        // Best-effort only; returns null if not available
        var location = assembly.Location;
        
        if (location.HasNoValue() 
         || File.Exists(location)
                .Not())
            return default;

        return File.GetLastWriteTimeUtc(location);
    }
    
    public RuntimeInfo GetRuntimeInfo()
    {
        return new RuntimeInfo
               {
                       Environment = GetEnvironment()
                     , Version     = GetVersion()
                     , Uptime      = DateTime.UtcNow - _startedAtUtc
               };
    }

    public string? GetConfigurationValue(string? key)
    {
        var builder = new ConfigurationBuilder().SetBasePath(_hostEnvironment.ContentRootPath)
                                                .AddJsonFile("appsettings.json", optional: true)
                                                .AddJsonFile($"appsettings.{_hostEnvironment.EnvironmentName}.json",
                                                             optional: true);

        var config = builder.Build();

        var values = config.AsEnumerable()
                           .Where(kvp => kvp.Value.HasValue())
                           .ToDictionary(kvp => kvp.Key
                                       , kvp => kvp.Value);
        if (key is not null)
        {
            values = values.Where(kvp => kvp.Key.Equals(key
                                                      , StringComparison.OrdinalIgnoreCase))
                           .ToDictionary(kvp => kvp.Key
                                       , kvp => kvp.Value);
        }
        return FormatConfigurationAsMarkdown(values);
    }
    
    private string FormatConfigurationAsMarkdown( IDictionary<string, string?> values)
    {
        if (values.Count == 0) return "No configuration values found.";

        var sb = new StringBuilder();

        sb.AppendLine($"# Configuration ({_hostEnvironment.EnvironmentName})");
        sb.AppendLine();
        sb.AppendLine("-----");
        sb.AppendLine();
        
        foreach (var group in values.OrderBy(valuePair => valuePair.Key)
                                    .GroupBy(valuePair => valuePair.Key.Split(':')[0]))
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();

            foreach (var item in group.OrderBy(valuePair => valuePair.Key))
            {
                var shortKey = item.Key;

                if (shortKey.StartsWith(group.Key + ":"))
                    shortKey = shortKey[(group.Key.Length + 1)..];

                sb.AppendLine($"- **{shortKey}**: `{item.Value}`");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
