namespace CognitivePlatform.Admin.Services;

public interface IReleaseVersionService
{
    ReleaseVersionState GetState();

    ReleaseVersionReservation Reserve(ReleaseReservationRequest request);

    ReleaseVersionReservation StartNew(ReleaseReservationRequest request, string reason);

    void Complete(string version);
}

public sealed record ReleaseReservationRequest( string Environment
                                              , string ComponentScope
                                              , string SourceIdentity
                                              , string ConfigurationIdentity
                                              , string Actor);

public sealed record ReleaseVersionReservation( string         Version
                                              , string         RunId
                                              , string         Environment
                                              , string         ComponentScope
                                              , string         SourceIdentity
                                              , string         ConfigurationIdentity
                                              , string         Actor
                                              , DateTimeOffset ReservedAtUtc);

public sealed record ReleaseVersionState( string                     LastIssuedVersion
                                        , string                     NextAvailableVersion
                                        , ReleaseVersionReservation? ActiveReservation);
