namespace CognitivePlatform.Admin.Services;

public interface IReleaseSourceIdentityService
{
    Task<ReleaseSourceIdentity> CreateSourceIdentityAsync(string componentScope);
}
