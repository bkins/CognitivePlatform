using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;

namespace CognitivePlatform.Api.Companion;

/// <summary>Reads original saved Markdown only. No file/database writes or synchronized document store.</summary>
public sealed partial class CompanionWorkspaceReader
{
    private const int MaxBytes = 1_048_576;
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().UsePipeTables().UseTaskLists().Build();

    public IReadOnlyList<CompanionFile> List(CompanionWorkspace workspace, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(workspace.RootPath);
        ValidateAncestors(root);
        var folders = new Stack<string>();
        folders.Push(root);
        var results = new List<CompanionFile>();
        var visited = 0;
        while (folders.TryPop(out var folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visited > 2000) throw new InvalidOperationException("Workspace directory bound exceeded.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint) || Path.GetFileName(entry).StartsWith('.')) continue;
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    if (Path.GetFileName(entry) != "node_modules") folders.Push(entry);
                    if (folders.Count + visited > 2000) throw new InvalidOperationException("Workspace directory bound exceeded.");
                }
                else if (MarkdownPath(entry))
                {
                    var info = new FileInfo(entry);
                    if (info.Length <= MaxBytes) results.Add(new(Path.GetRelativePath(root, entry).Replace('\\', '/'), info.Length, info.LastWriteTimeUtc));
                    if (results.Count > 1000) throw new InvalidOperationException("Workspace file bound exceeded.");
                }
            }
        }
        return results.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
    }

    public async Task<CompanionDocument> ReadAsync(CompanionWorkspace workspace, string relativePath, CancellationToken cancellationToken)
    {
        var path = Resolve(workspace, relativePath);
        ValidateAncestors(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 8192, true);
        if (stream.Length > MaxBytes) throw new InvalidOperationException("Document exceeds companion size limit.");
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (bytes.Length + count > MaxBytes) throw new InvalidOperationException("Document exceeds companion size limit.");
            bytes.Write(buffer, 0, count);
        }
        var content = bytes.ToArray();
        var encoding = content.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }) ? new UnicodeEncoding(false, true, true)
                     : content.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }) ? new UnicodeEncoding(true, true, true)
                     : (Encoding)new UTF8Encoding(false, true);
        var text = encoding.GetString(content).TrimStart('\uFEFF');
        if (text.Length > 200_000) throw new InvalidOperationException("Document exceeds companion text limit.");
        var firstNewline = text.IndexOf('\n');
        var firstLine = (firstNewline < 0 ? text : text[..firstNewline]).TrimEnd('\r');
        var marker = IdentityMarker().Match(firstLine);
        Guid? documentId = marker.Success && Guid.TryParse(marker.Groups["id"].Value, out var parsed) && parsed != Guid.Empty ? parsed : null;
        var previewText = documentId is null ? text : firstNewline < 0 ? "" : text[(firstNewline + 1)..];
        var syntax = Markdig.Markdown.Parse(previewText, Pipeline);
        foreach (var link in syntax.Descendants<Markdig.Syntax.Inlines.LinkInline>())
        {
            link.Url = "";
            link.IsImage = false;
        }
        var html = Markdig.Markdown.ToHtml(syntax, Pipeline);
        return new(relativePath.Replace('\\', '/'), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))), DateTimeOffset.UtcNow, documentId, text, html);
    }

    public static string Resolve(CompanionWorkspace workspace, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Length > 1024 || Path.IsPathRooted(relativePath)
         || relativePath.Contains(':') || relativePath.Split(['/', '\\']).Any(segment => segment is "." or ".." || segment.StartsWith('.')))
            throw new UnauthorizedAccessException("Path is outside the shared workspace.");
        var root = Path.GetFullPath(workspace.RootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || !MarkdownPath(path))
            throw new UnauthorizedAccessException("Only shared Markdown documents are accessible.");
        return path;
    }

    private static bool MarkdownPath(string path) => Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase)
                                                || Path.GetExtension(path).Equals(".markdown", StringComparison.OrdinalIgnoreCase);
    private static void ValidateAncestors(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) throw new UnauthorizedAccessException("Linked paths are not shared.");
    }

    [GeneratedRegex(@"\A<!--[ \t]*axiom:documentId=(?<id>[0-9a-fA-F-]{36})[ \t]*-->[ \t]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityMarker();
}
