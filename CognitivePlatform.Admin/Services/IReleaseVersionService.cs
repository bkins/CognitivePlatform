namespace CognitivePlatform.Admin.Services;

public interface IReleaseVersionService
{
    string GetCurrentVersion();

    string IncrementBuild();
}
