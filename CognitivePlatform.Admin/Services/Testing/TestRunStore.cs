using System.Text.Json;
using System.Text.Json.Serialization;

namespace CognitivePlatform.Admin.Services.Testing;

public sealed class TestRunStore
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
      , Converters = { new JsonStringEnumConverter() }
    };

    private readonly TestOperationsOptions _options;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, int> _logCounts = new();

    public TestRunStore(TestOperationsOptions options)
    {
        _options = options;
        var root = Path.GetFullPath(options.RootPath);
        foreach (var protectedRoot in new[] { @"C:\CP\Data", @"C:\CP\Deploy", options.RepositoryRoot })
        {
            var forbidden = Path.GetFullPath(protectedRoot).TrimEnd(Path.DirectorySeparatorChar);
            if (root.Equals(forbidden, StringComparison.OrdinalIgnoreCase) || root.StartsWith(forbidden + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Test operations output must be outside repositories, deployments and application data.");
        }
        _options.RootPath = root;
        if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Operations root cannot be a link.");
    }

    public string DirectoryFor(Guid id) => Path.Combine(_options.RootPath, id.ToString("N"));

    public string BuildDirectoryFor(Guid id) => Path.Combine(_options.RepositoryRoot, "CognitivePlatform", "test-output", "admin-validation", id.ToString("N"));

    public string Redact(string text)
    {
        if (string.IsNullOrEmpty(_options.Secret)) return text;
        var encoded = JsonSerializer.Serialize(_options.Secret)[1..^1];
        return text.Replace(_options.Secret, "[REDACTED]", StringComparison.Ordinal)
                   .Replace(encoded, "[REDACTED]", StringComparison.Ordinal)
                   .Replace(System.Security.SecurityElement.Escape(_options.Secret)!, "[REDACTED]", StringComparison.Ordinal);
    }

    public TestRun Clone(TestRun run) => JsonSerializer.Deserialize<TestRun>(JsonSerializer.Serialize(run, JsonOptions), JsonOptions)!;

    public List<TestRun> Load()
    {
        if (!Directory.Exists(_options.RootPath)) return [];
        var result = new List<TestRun>();
        foreach (var directory in Directory.EnumerateDirectories(_options.RootPath))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) continue;
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Run directory cannot be a link.");
            var path = Path.Combine(directory, "run.json");
            if (!File.Exists(path)) continue;
            var run = JsonSerializer.Deserialize<TestRun>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException("Unreadable test run.");
            if (run.Id != id) throw new InvalidDataException("Run identity mismatch.");
            if (!run.FinishedAt.HasValue)
            {
                run.Status = TestRunStatus.Interrupted;
                run.FinishedAt = DateTimeOffset.UtcNow;
                run.Error = "Admin restarted before the run completed; execution was not replayed.";
                Save(run);
            }
            result.Add(run);
        }
        return result;
    }

    public void Save(TestRun run)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(DirectoryFor(run.Id));
            var path = Path.Combine(DirectoryFor(run.Id), "run.json");
            File.WriteAllText(path + ".tmp", Redact(JsonSerializer.Serialize(run, JsonOptions)));
            File.Move(path + ".tmp", path, true);
        }
    }

    public bool Append(Guid id, string line)
    {
        lock (_gate)
        {
            var directory = DirectoryFor(id);
            var path = Path.Combine(directory, "log.jsonl");
            var count = _logCounts.TryGetValue(id, out var retained) ? retained : File.Exists(path) ? File.ReadLines(path).Count() : 0;
            if (count >= Math.Clamp(_options.MaxLogLines, 1, 100000))
            {
                File.WriteAllText(Path.Combine(directory, "truncated.flag"), "Log limit reached; use structured results.");
                return false;
            }
            var safe = Redact(line);
            if (safe.Length > 4000) safe = safe[..4000] + " [line truncated]";
            File.AppendAllText(path, JsonSerializer.Serialize(safe) + Environment.NewLine);
            _logCounts[id] = count + 1;
            return true;
        }
    }

    public TestLogPage Logs(Guid id, int after, int limit)
    {
        lock (_gate)
        {
            if (after < 0 || limit is < 1 or > 500) throw new ArgumentException("Cursor must be nonnegative and limit between 1 and 500.");
            var path = Path.Combine(DirectoryFor(id), "log.jsonl");
            var lines = File.Exists(path) ? File.ReadAllLines(path) : [];
            if (after > lines.Length) throw new ArgumentException("Cursor exceeds the retained log.");
            var page = lines.Skip(after).Take(limit).Select(line => JsonSerializer.Deserialize<string>(line)!).ToArray();
            return new TestLogPage(page, after + page.Length, File.Exists(Path.Combine(DirectoryFor(id), "truncated.flag")));
        }
    }

    private static bool ContainsLink(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return true;
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) return true;
            if ((attributes & FileAttributes.Directory) != 0 && ContainsLink(path)) return true;
        }
        return false;
    }

    public void Trim(IReadOnlyList<TestRun> retained)
    {
        foreach (var run in retained.OrderByDescending(run => run.StartedAt).Skip(Math.Max(1, _options.MaxRetainedRuns)))
        {
            if (!run.FinishedAt.HasValue) continue;
            var directory = Path.GetFullPath(DirectoryFor(run.Id));
            var expectedParent = Path.GetFullPath(_options.RootPath).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(directory), expectedParent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe retention path.");
            if (!Directory.Exists(directory)) continue;
            if (ContainsLink(directory)) continue;
            Directory.Delete(directory, true);
            var build = Path.GetFullPath(BuildDirectoryFor(run.Id));
            var buildParent = Path.GetFullPath(Path.Combine(_options.RepositoryRoot, "CognitivePlatform", "test-output", "admin-validation"));
            if (Path.GetDirectoryName(build) == buildParent && Directory.Exists(build) && !ContainsLink(build)) Directory.Delete(build, true);
            _logCounts.Remove(run.Id);
        }
    }
}
