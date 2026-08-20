using System.Net.Http.Json;
using System.Text.Json;

namespace CognitivePlatform.DemoSetup;

public sealed class DemoReset
{
    private static readonly JsonSerializerOptions JsonOptions = new()
                                                                {
                                                                    PropertyNamingPolicy        = JsonNamingPolicy.CamelCase
                                                                  , PropertyNameCaseInsensitive = true
                                                                };

    private readonly HttpClient _http;
    private readonly string     _resetSession = Guid.NewGuid().ToString("N");

    public DemoReset(HttpClient http) => _http = http;

    public async Task<int> RunAsync()
    {
        Console.WriteLine("[DEMO] Starting demo data reset...");
        Console.WriteLine();

        if (!File.Exists(SeedManifest.FileName))
        {
            Console.WriteLine("[DEMO] No manifest found. Running pattern-based cleanup...");
            Console.WriteLine();
            return await PatternCleanupAsync();
        }

        var json     = await File.ReadAllTextAsync(SeedManifest.FileName);
        var manifest = JsonSerializer.Deserialize<SeedManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (manifest is null)
        {
            Console.Error.WriteLine("[DEMO] ERROR: Could not parse manifest.");
            return 1;
        }

        Console.WriteLine($"[DEMO] Loaded manifest (seeded {manifest.SeededAt:yyyy-MM-dd HH:mm} UTC).");
        Console.WriteLine();

        // -- Delete tasks ----------------------------------------------------
        Console.WriteLine("[DEMO] Deleting tasks...");
        var taskCount = 0;

        foreach (var id in manifest.TaskIds)
        {
            var resp = await _http.DeleteAsync($"api/tasks/{id}");
            if (resp.IsSuccessStatusCode) taskCount++;
        }

        Console.WriteLine($"  ✓ Deleted {taskCount}/{manifest.TaskIds.Count} tasks");

        // -- Delete journal entries (requires LLM path) ----------------------
        Console.WriteLine("[DEMO] Deleting journal entries...");
        var journalCount = 0;

        foreach (var id in manifest.JournalEntryIds)
        {
            var resp = await ConverseAsync(_resetSession, $"Delete journal entry {id}, demo data cleanup", fastPath: false);
            if (resp?.Success == true) journalCount++;
        }

        if (manifest.JournalEntryIds.Count > 0 && journalCount == 0)
            Console.WriteLine($"  ○ Could not delete journal entries (LLM may be unavailable). {manifest.JournalEntryIds.Count} entries with [DEMO] prefix remain and can be deleted manually.");
        else
            Console.WriteLine($"  ✓ Deleted {journalCount}/{manifest.JournalEntryIds.Count} journal entries");

        // -- Delete conversations (seed session + demo conversations) --------
        Console.WriteLine("[DEMO] Deleting conversations...");
        var convCount = 0;

        var allConvIds = new List<string>(manifest.ConversationIds);
        if (manifest.SeedSessionId.HasValue())
            allConvIds.Add(manifest.SeedSessionId);
        allConvIds.Add(_resetSession);

        foreach (var id in allConvIds)
        {
            var resp = await _http.DeleteAsync($"api/conversation/{id}");
            if (resp.IsSuccessStatusCode) convCount++;
        }

        Console.WriteLine($"  ✓ Deleted {convCount - 1}/{manifest.ConversationIds.Count} demo conversations");

        // -- Delete daily record ---------------------------------------------
        if (manifest.HasDailyRecord)
        {
            Console.WriteLine("[DEMO] Removing daily record...");

            var today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");

            if (today == manifest.DailyRecordDate)
            {
                var resp = await ConverseAsync(_resetSession, "Delete today's daily record");
                Console.WriteLine(resp?.Success == true
                                      ? "  ✓ Deleted today's daily record"
                                      : "  ○ Daily record already removed or belongs to a different day");
            }
            else
            {
                Console.WriteLine($"  ○ Skipped: daily record was for {manifest.DailyRecordDate} (not today)");
            }
        }

        // -- Clean up manifest -----------------------------------------------
        File.Delete(SeedManifest.FileName);
        Console.WriteLine();
        Console.WriteLine("[DEMO] Reset complete. Manifest removed.");

        return 0;
    }

    // =========================================================================
    // Fallback: pattern-based cleanup when no manifest exists
    // =========================================================================

    private async Task<int> PatternCleanupAsync()
    {
        // Tasks
        Console.WriteLine("[DEMO] Cleaning up tasks with [DEMO] prefix...");

        var tasks     = await GetTasksAsync();
        var demoTasks = tasks.Where(task => task.ShortDescription.StartsWith("[DEMO]")).ToList();
        var taskCount = 0;

        foreach (var task in demoTasks)
        {
            var resp = await _http.DeleteAsync($"api/tasks/{task.Id}");
            if (resp.IsSuccessStatusCode) taskCount++;
        }

        Console.WriteLine($"  ✓ Deleted {taskCount}/{demoTasks.Count} tasks");

        // Journal entries
        Console.WriteLine("[DEMO] Cleaning up journal entries with [DEMO] prefix...");

        var entries     = await GetJournalEntriesAsync();
        var demoEntries = entries.Where(entry => entry.Text.StartsWith("[DEMO]")).ToList();
        var journalCount = 0;

        foreach (var entry in demoEntries)
        {
            var resp = await ConverseAsync(_resetSession, $"Delete journal entry {entry.Id}, demo data cleanup", fastPath: false);
            if (resp?.Success == true) journalCount++;
        }

        if (demoEntries.Count > 0 && journalCount == 0)
            Console.WriteLine($"  ○ Could not delete journal entries (LLM may be unavailable). {demoEntries.Count} entries with [DEMO] prefix remain.");
        else
            Console.WriteLine($"  ✓ Deleted {journalCount}/{demoEntries.Count} journal entries");

        await _http.DeleteAsync($"api/conversation/{_resetSession}");

        Console.WriteLine();
        Console.WriteLine("[DEMO] Pattern cleanup complete.");
        return 0;
    }

    // =========================================================================
    // HTTP helpers
    // =========================================================================

    private async Task<ConverseResponse?> ConverseAsync( string sessionId
                                                        , string input
                                                        , bool   fastPath = true )
    {
        var request = new ConverseRequest
                      {
                          SessionId = sessionId
                        , Input     = input
                        , FastPath  = fastPath
                        , Streaming = false
                      };

        try
        {
            var response = await _http.PostAsJsonAsync("api/conversation/converse", request, JsonOptions);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<ConverseResponse>(JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<ApiTaskItem>> GetTasksAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ApiTaskItem>>("api/tasks", JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task<List<ApiJournalEntry>> GetJournalEntriesAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ApiJournalEntry>>("api/journals", JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
