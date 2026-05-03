using System.Text;
using System.Text.Json;
using CognitivePlatform.Api.SystemPromptLogging.Models;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.SystemPromptLogging;

/// <summary>
/// A simple file-based prompt logger for development and debugging purposes.
/// Logs prompts along with metadata to timestamped files, and maintains an index for easy reference.
/// </summary>
public sealed class PromptLogger : IPromptLogger
{
    private readonly PromptLoggingOptions _options;
    private readonly object               _lock = new();
    private readonly string               _promptDir;
    private readonly string               _indexDir;

    public PromptLogger(IOptions<PromptLoggingOptions> options)
    {
        _options = options.Value;

        var basePath = Path.Combine(AppContext.BaseDirectory, _options.BasePath);

        _promptDir = Path.Combine(basePath, "Prompts");
        _indexDir  = Path.Combine(basePath, "Index");

        Directory.CreateDirectory(_promptDir);
        Directory.CreateDirectory(_indexDir);
    }

    public void Log(string description
                  , string prompt
                  , string? provider = null
                  , string? model = null)
    {
        if (prompt.HasNoValue()) return;

        lock (_lock)
        {
            if (_options.Enabled.Not()) return;

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var safeDesc  = Sanitize(description);

            var fileBase = $"{timestamp}_{safeDesc}";

            var promptFileName = $"{fileBase}.txt";
            var indexFileName  = $"{fileBase}.json";

            var promptPath = Path.Combine(_promptDir, promptFileName);
            var indexPath  = Path.Combine(_indexDir , indexFileName);

            Console.WriteLine($"[PromptLogger] Logging prompt: {description} (Provider: {provider ?? "N/A"}, Model: {model ?? "N/A"})");
            Console.WriteLine($"[PromptLogger] Prompt file: {promptPath}");
            
            File.WriteAllText(promptPath
                            , BuildPromptContent(description, prompt, provider, model)
                            , Encoding.UTF8);

            var entry = new PromptLogEntry
                        {
                                Description = description
                              , Timestamp   = timestamp
                              , PromptFile  = promptFileName
                              , Provider    = provider
                              , Model       = model
                        };

            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions
                                                       {
                                                               WriteIndented = true
                                                       });

            File.WriteAllText(indexPath, json, Encoding.UTF8);

            Cleanup();
        }
    }

    private string BuildPromptContent( string  description
                                     , string  prompt
                                     , string? provider = null
                                     , string? model    = null )
    {
        var sb = new StringBuilder();

        var providerLine = provider.HasValue()
                                   ? $"Provider: {provider}"
                                   : "Provider: <Not specified>";
        var modelLine = model.HasValue()
                                ? $"Model: {model}"
                                : "Model: <Not specified>";

        var separator = new string('-', 80);
        
        sb.AppendLine($"Description: {description}");
        sb.AppendLine($"Timestamp (UTC): {DateTime.UtcNow:O}");
        sb.AppendLine(providerLine);
        sb.AppendLine(modelLine);
        sb.AppendLine(separator);
        sb.AppendLine(prompt);

        return sb.ToString();
    }

    private void Cleanup()
    {
        var indexFiles = new DirectoryInfo(_indexDir).GetFiles("*.json")
                                                     .OrderByDescending(fileInfo => fileInfo.CreationTimeUtc)
                                                     .ToList();

        if (indexFiles.Count <= _options.MaxFiles) return;

        var toDelete = indexFiles.Skip(_options.MaxFiles);

        foreach (var indexFile in toDelete)
        {
            try
            {
                var entry = JsonSerializer.Deserialize<PromptLogEntry>(
                    File.ReadAllText(indexFile.FullName));

                indexFile.Delete();

                if (entry is null) continue;
                
                var promptPath = Path.Combine(_promptDir, entry.PromptFile);

                if (File.Exists(promptPath)) File.Delete(promptPath);
            }
            catch { /* Never crash logging */}
        }
    }

    private static string Sanitize(string input)
    {
        if (input.HasNoValue()) return "prompt";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(input.Where(value => invalid.Contains(value).Not())
                                      .ToArray());

        cleaned = cleaned.Replace(' ', '_');

        if (cleaned.Length > 40) cleaned = cleaned[..40];

        return cleaned;
    }
}


