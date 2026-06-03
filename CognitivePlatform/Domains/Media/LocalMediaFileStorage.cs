namespace CognitivePlatform.Api.Domains.Media;

public sealed class LocalMediaFileStorage : IMediaFileStorage
{
    public async Task WriteAsync(string path, Stream content, CancellationToken ct = default)
    {
        await using var fileStream = new FileStream(path
                                                  , FileMode.Create
                                                  , FileAccess.Write
                                                  , FileShare.None);
        await content.CopyToAsync(fileStream, ct);
    }

    public Stream OpenRead(string path)
        => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    public void Delete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    public bool Exists(string path)
        => File.Exists(path);

    public void EnsureDirectory(string directoryPath)
        => Directory.CreateDirectory(directoryPath);
}
