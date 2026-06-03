namespace CognitivePlatform.Api.Domains.Media;

public interface IMediaFileStorage
{
    Task   WriteAsync     (string path, Stream content, CancellationToken ct = default);
    Stream OpenRead       (string path);
    void   Delete         (string path);
    bool   Exists         (string path);
    void   EnsureDirectory(string directoryPath);
}
