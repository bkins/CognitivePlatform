using CognitivePlatform.Api.Integrations.FileSync.Models;

namespace CognitivePlatform.Api.Integrations.FileSync;

public interface ILocalFileSystem
{
    Task<IReadOnlyList<FileEntry>> ListAsync     (string path,                 CancellationToken ct = default);
    Task<Stream>                   OpenReadAsync (string path,                 CancellationToken ct = default);
    Task                           WriteAsync    (string path, Stream content, CancellationToken ct = default);
}
