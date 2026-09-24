using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CognitivePlatform.Admin.Services;

public sealed class ReleaseVersionService : IReleaseVersionService
{
    private static readonly object VersionLock = new();
    private static readonly Regex VersionPattern = new(@"(?<!\d)(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)\.(?<build>\d+)(?!\d)"
                                                     , RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _versionJsonPath;
    private readonly string _artifactsRoot;
    private readonly string _deploymentStatePath;
    private readonly string _auditPath;
    private readonly string _mutexName;

    public ReleaseVersionService(IConfiguration configuration)
    {
        _versionJsonPath = configuration["SystemPaths:VersionJsonPath"]
                           ?? @"C:\Users\benho\source\repos\CP\CP.Workbench\version.json";
        _artifactsRoot = configuration["SystemPaths:ArtifactsRoot"] ?? @"C:\CP\Artifacts";
        _deploymentStatePath = configuration["SystemPaths:DeploymentStatePath"]
                               ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                                             , "ReleaseConsole", "deployment-state.json");
        _auditPath = configuration["SystemPaths:ReleaseVersionAuditPath"]
                     ?? Path.Combine(Path.GetDirectoryName(_versionJsonPath) ?? AppContext.BaseDirectory
                                   , "release-version-audit.jsonl");
        var authorityHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(_versionJsonPath).ToUpperInvariant())));
        _mutexName = $@"Local\CP.ReleaseVersion.{authorityHash[..24]}";
    }

    public ReleaseVersionState GetState()
    {
        return ExecuteLocked(authority => CreateState(authority));
    }

    public ReleaseVersionReservation Reserve(ReleaseReservationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteLocked(authority => ReserveCore(authority, request));
    }

    public ReleaseVersionReservation StartNew(ReleaseReservationRequest request, string reason)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("An abandonment reason is required.", nameof(reason));

        return ExecuteLocked(authority =>
        {
            if (authority.ActiveReservation is not null)
            {
                AppendAudit("abandoned", authority.ActiveReservation, reason);
                authority.ActiveReservation = null;
                WriteAuthority(authority);
            }

            return ReserveCore(authority, request);
        });
    }

    public void Complete(string version)
    {
        ExecuteLocked(authority =>
        {
            if (authority.ActiveReservation?.Version == version)
            {
                AppendAudit("completed", authority.ActiveReservation, "Release action completed successfully.");
                authority.ActiveReservation = null;
                WriteAuthority(authority);
            }

            return true;
        });
    }

    private ReleaseVersionReservation ReserveCore(VersionAuthority authority, ReleaseReservationRequest request)
    {
        if (authority.ActiveReservation is not null)
        {
            if (Matches(authority.ActiveReservation, request)) return authority.ActiveReservation;

            throw new InvalidOperationException(
                $"Version {authority.ActiveReservation.Version} is reserved for a different release identity. " +
                "Resume that release or select Start New Release; the reserved version cannot be reused.");
        }

        var highestBuild = FindHighestIssuedBuild(authority);
        var version = FormatVersion(authority.Major, authority.Minor, authority.Patch, checked(highestBuild + 1));
        var reservation = new ReleaseVersionReservation( version
                                                       , Guid.NewGuid().ToString("N")
                                                       , request.Environment
                                                       , request.ComponentScope
                                                       , request.SourceIdentity
                                                       , request.ConfigurationIdentity
                                                       , request.Actor
                                                       , DateTimeOffset.UtcNow);
        authority.Build = highestBuild + 1;
        authority.ActiveReservation = reservation;
        WriteAuthority(authority);
        AppendAudit("reserved", reservation, "Version reserved atomically.");
        return reservation;
    }

    private ReleaseVersionState CreateState(VersionAuthority authority)
    {
        var highestBuild = FindHighestIssuedBuild(authority);
        return new ReleaseVersionState( FormatVersion(authority.Major, authority.Minor, authority.Patch, highestBuild)
                                      , FormatVersion(authority.Major, authority.Minor, authority.Patch, checked(highestBuild + 1))
                                      , authority.ActiveReservation);
    }

    private int FindHighestIssuedBuild(VersionAuthority authority)
    {
        var highestBuild = authority.Build;
        highestBuild = Math.Max(highestBuild, FindHighestBuildInDirectory(authority));
        highestBuild = Math.Max(highestBuild, FindHighestBuildInFile(_deploymentStatePath, authority));
        highestBuild = Math.Max(highestBuild, FindHighestBuildInFile(_auditPath, authority));
        return highestBuild;
    }

    private int FindHighestBuildInDirectory(VersionAuthority authority)
    {
        if (!Directory.Exists(_artifactsRoot)) return 0;

        try
        {
            return Directory.EnumerateDirectories(_artifactsRoot, "*", SearchOption.AllDirectories)
                            .Select(Path.GetFileName)
                            .Select(name => TryReadBuild(name, authority))
                            .DefaultIfEmpty(0)
                            .Max();
        }
        catch (IOException exception)
        {
            throw new InvalidDataException($"Unable to reconcile release artifacts at '{_artifactsRoot}'.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidDataException($"Unable to reconcile release artifacts at '{_artifactsRoot}'.", exception);
        }
    }

    private static int TryReadBuild(string? text, VersionAuthority authority)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var match = VersionPattern.Match(text);
        if (!match.Success) return 0;
        if (int.Parse(match.Groups["major"].Value) != authority.Major) return 0;
        if (int.Parse(match.Groups["minor"].Value) != authority.Minor) return 0;
        if (int.Parse(match.Groups["patch"].Value) != authority.Patch) return 0;
        return int.Parse(match.Groups["build"].Value);
    }

    private static int FindHighestBuildInFile(string path, VersionAuthority authority)
    {
        if (!File.Exists(path)) return 0;

        try
        {
            return VersionPattern.Matches(File.ReadAllText(path))
                                 .Select(match => TryReadBuild(match.Value, authority))
                                 .DefaultIfEmpty(0)
                                 .Max();
        }
        catch (IOException exception)
        {
            throw new InvalidDataException($"Unable to reconcile release evidence at '{path}'.", exception);
        }
    }

    private VersionAuthority ReadAuthority()
    {
        if (!File.Exists(_versionJsonPath))
            throw new InvalidDataException($"Release version authority '{_versionJsonPath}' does not exist.");

        try
        {
            return JsonSerializer.Deserialize<VersionAuthority>(File.ReadAllText(_versionJsonPath))
                   ?? throw new InvalidDataException($"Release version authority '{_versionJsonPath}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Release version authority '{_versionJsonPath}' is invalid; no version was issued.", exception);
        }
    }

    private void WriteAuthority(VersionAuthority authority)
    {
        var directory = Path.GetDirectoryName(_versionJsonPath);
        if (directory is not null) Directory.CreateDirectory(directory);
        var temporaryPath = $"{_versionJsonPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(authority, WriteOptions));
            File.Move(temporaryPath, _versionJsonPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private void AppendAudit(string eventName, ReleaseVersionReservation reservation, string details)
    {
        var directory = Path.GetDirectoryName(_auditPath);
        if (directory is not null) Directory.CreateDirectory(directory);
        var auditEvent = new
        {
            Event        = eventName
          , reservation.Version
          , reservation.RunId
          , reservation.Environment
          , reservation.ComponentScope
          , reservation.SourceIdentity
          , reservation.ConfigurationIdentity
          , reservation.Actor
          , TimestampUtc = DateTimeOffset.UtcNow
          , Details      = details
        };
        File.AppendAllText(_auditPath, JsonSerializer.Serialize(auditEvent) + Environment.NewLine);
    }

    private T ExecuteLocked<T>(Func<VersionAuthority, T> action)
    {
        lock (VersionLock)
        {
            using var mutex = new Mutex(initiallyOwned: false, _mutexName);
            var acquired = false;
            try
            {
                try
                {
                    acquired = mutex.WaitOne(TimeSpan.FromSeconds(30));
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }

                if (!acquired) throw new TimeoutException("Timed out waiting for the release version authority lock.");
                return action(ReadAuthority());
            }
            finally
            {
                if (acquired) mutex.ReleaseMutex();
            }
        }
    }

    private static bool Matches(ReleaseVersionReservation reservation, ReleaseReservationRequest request) =>
        string.Equals(reservation.Environment, request.Environment, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(reservation.ComponentScope, request.ComponentScope, StringComparison.Ordinal) &&
        string.Equals(reservation.SourceIdentity, request.SourceIdentity, StringComparison.Ordinal) &&
        string.Equals(reservation.ConfigurationIdentity, request.ConfigurationIdentity, StringComparison.Ordinal);

    private static string FormatVersion(int major, int minor, int patch, int build) => $"{major}.{minor}.{patch}.{build}";

    private sealed class VersionAuthority
    {
        public int                        Major             { get; set; }
        public int                        Minor             { get; set; }
        public int                        Patch             { get; set; }
        public int                        Build             { get; set; }
        public ReleaseVersionReservation? ActiveReservation { get; set; }
    }
}
