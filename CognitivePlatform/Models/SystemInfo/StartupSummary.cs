using CognitivePlatform.Api.System;
using CognitivePlatform.Api.SystemInfo;

namespace CognitivePlatform.Api.Models.SystemInfo;

public record StartupSummary
{
    public required IReadOnlyList<string> Urls         { get; init; }
    public required SystemEnvironmentInfo EnvInfo      { get; init; }
    public required SystemVersionInfo     VerInfo      { get; init; }
    public required SystemService         SysInfo      { get; init; }
    public required string                DefaultModel { get; init; }
    public          string                Provider     { get; set; }

    public override string ToString()
    {
        var indent      = "        ";
        var tab         = "\t";
        var urls        = string.Join("\n", Urls.Select(url => $"{tab}{indent}{url}"));
        var scalarUrls  = string.Join("\n", Urls.Select(url => $"{tab}{indent}{url}/Scalar"));
        var shortCommit = VerInfo.InformationalVersion?.Split('+').ElementAtOrDefault(1)?[..8] ?? "unknown";
        
        return $"""
                {tab}Cognitive Platform API — Startup
                {tab}────────────────────────────────────────
                {tab}Environment:    {EnvInfo.EnvironmentName}
                {tab}Machine:        {EnvInfo.MachineName}
                {tab}Process ID:     {EnvInfo.ProcessId}
                {tab}Started (UTC):  {EnvInfo.StartedAtUtc.ToLocalTime()}
                {tab}
                {tab}Listening on:
                {urls}
                {tab}
                {tab}Scalar API:
                {scalarUrls}
                {tab}
                {tab}Database:       {EnvInfo.DatabasePath}
                {tab}
                {tab}Version:        {VerInfo.Version}
                {tab}Build:          {VerInfo.BuildConfiguration}  ({VerInfo.BuildTimeUtc.ToLocalTime()})
                {tab}Commit:         {shortCommit}
                {tab}
                {tab}LLM Model:      {DefaultModel}
                {tab}Provider:       {Provider}
                {tab}────────────────────────────────────────
                """;
    }
}