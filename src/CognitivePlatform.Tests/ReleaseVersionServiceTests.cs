using CognitivePlatform.Admin.Services;
using Microsoft.Extensions.Configuration;

namespace CognitivePlatform.Tests;

public sealed class ReleaseVersionServiceTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), $"release-version-{Guid.NewGuid():N}");

    private string VersionJsonPath      => Path.Combine(_testRoot, "version.json");
    private string ArtifactsRoot        => Path.Combine(_testRoot, "artifacts");
    private string DeploymentStatePath  => Path.Combine(_testRoot, "deployment-state.json");
    private string ReservationAuditPath => Path.Combine(_testRoot, "release-version-audit.jsonl");

    [Fact]
    public void GetState_WhenVersionFileExists_DoesNotConsumeAVersion()
    {
        WriteVersion(87);
        var service = CreateService();

        var state = service.GetState();

        Assert.Equal("1.3.0.87", state.LastIssuedVersion);
        Assert.Equal("1.3.0.88", state.NextAvailableVersion);
        Assert.Null(state.ActiveReservation);
        Assert.Contains("\"Build\": 87", File.ReadAllText(VersionJsonPath));
    }

    [Fact]
    public void Reserve_WhenArtifactsAndDeploymentAreAheadOfCounter_UsesNextMonotonicVersion()
    {
        WriteVersion(113);
        Directory.CreateDirectory(Path.Combine(ArtifactsRoot, "API", "1.3.0.117"));
        File.WriteAllText(DeploymentStatePath, "{ \"API_Dev\": { \"version\": \"1.3.0.116\" } }");
        var service = CreateService();

        var reservation = service.Reserve(Request());

        Assert.Equal("1.3.0.118", reservation.Version);
        Assert.Contains("\"Build\": 118", File.ReadAllText(VersionJsonPath));
    }

    [Fact]
    public void Reserve_WhenIdentityMatchesActiveReservation_ReusesTheReservation()
    {
        WriteVersion(117);
        var service = CreateService();
        var request = Request();

        var first = service.Reserve(request);
        var retry = CreateService().Reserve(request);

        Assert.Equal(first.Version, retry.Version);
        Assert.Equal(first.RunId, retry.RunId);
        Assert.Equal("1.3.0.119", CreateService().GetState().NextAvailableVersion);
    }

    [Fact]
    public void Reserve_WhenIdentityDiffersFromActiveReservation_BlocksReuse()
    {
        WriteVersion(117);
        var service = CreateService();
        service.Reserve(Request());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateService().Reserve(Request(sourceIdentity: "different-commit")));

        Assert.Contains("Start New Release", exception.Message);
        Assert.Equal("1.3.0.118", CreateService().GetState().ActiveReservation?.Version);
    }

    [Fact]
    public void StartNew_WhenReservationExists_AuditsAbandonmentAndNeverReusesVersion()
    {
        WriteVersion(117);
        var service = CreateService();
        service.Reserve(Request());

        var replacement = service.StartNew(Request(sourceIdentity: "different-commit"), "Source changed");

        Assert.Equal("1.3.0.119", replacement.Version);
        var audit = File.ReadAllText(ReservationAuditPath);
        Assert.Contains("abandoned", audit);
        Assert.Contains("1.3.0.118", audit);
        Assert.Contains("Source changed", audit);
    }

    [Fact]
    public async Task Reserve_WhenCalledConcurrently_ReturnsOneReservation()
    {
        WriteVersion(117);
        var request = Request();

        var reservations = await Task.WhenAll(
            Enumerable.Range(0, 12)
                      .Select(_ => Task.Run(() => CreateService().Reserve(request))));

        Assert.Single(reservations.Select(reservation => reservation.RunId).Distinct());
        Assert.All(reservations, reservation => Assert.Equal("1.3.0.118", reservation.Version));
    }

    [Fact]
    public void GetState_WhenVersionFileIsCorrupt_FailsClosed()
    {
        Directory.CreateDirectory(_testRoot);
        File.WriteAllText(VersionJsonPath, "not-json");

        var exception = Assert.Throws<InvalidDataException>(() => CreateService().GetState());

        Assert.Contains("version authority", exception.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, recursive: true);
    }

    private void WriteVersion(int build)
    {
        Directory.CreateDirectory(_testRoot);
        File.WriteAllText(VersionJsonPath, $"{{ \"Major\": 1, \"Minor\": 3, \"Patch\": 0, \"Build\": {build} }}");
    }

    private ReleaseVersionService CreateService()
    {
        var configuration = new ConfigurationBuilder()
                           .AddInMemoryCollection(new Dictionary<string, string?>
                           {
                               ["SystemPaths:VersionJsonPath"]         = VersionJsonPath
                             , ["SystemPaths:ArtifactsRoot"]           = ArtifactsRoot
                             , ["SystemPaths:DeploymentStatePath"]     = DeploymentStatePath
                             , ["SystemPaths:ReleaseVersionAuditPath"] = ReservationAuditPath
                           })
                           .Build();
        return new ReleaseVersionService(configuration);
    }

    private static ReleaseReservationRequest Request(string sourceIdentity = "commit-abc") =>
        new("DEV", "API,Laa,LaaWindows,CocoAPI", sourceIdentity, "config-123", "benho");
}
