using CognitivePlatform.Api.Integrations.FileSync.Models;

namespace CognitivePlatform.Api.Integrations.FileSync;

public sealed class LocalFileSystem : ILocalFileSystem
{
    public Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken ct = default)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
            return Task.FromResult<IReadOnlyList<FileEntry>>([]);

        var files   = directory.GetFiles("*", SearchOption.AllDirectories);
        var entries = new List<FileEntry>(files.Length);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            entries.Add(new FileEntry
                        {
                            RelativePath = Path.GetRelativePath(path, file.FullName)
                          , SizeBytes    = file.Length
                          , LastModified = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)
                        });
        }

        return Task.FromResult<IReadOnlyList<FileEntry>>(entries);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
    {
        Stream stream = new FileStream( path
                                      , FileMode.Open
                                      , FileAccess.Read
                                      , FileShare.Read
                                      , bufferSize: 4096
                                      , useAsync: true );
        return Task.FromResult(stream);
    }

    public async Task WriteAsync(string path, Stream content, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var output = new FileStream( path
                                               , FileMode.Create
                                               , FileAccess.Write
                                               , FileShare.None
                                               , bufferSize: 4096
                                               , useAsync: true );
        await content.CopyToAsync(output, ct);
    }
}
