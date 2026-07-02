using System.Net.Http.Json;
using System.Text.Json;

namespace CognitivePlatform.DemoSetup;

public sealed class DemoSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
                                                                {
                                                                    PropertyNamingPolicy        = JsonNamingPolicy.CamelCase
                                                                  , PropertyNameCaseInsensitive = true
                                                                };

    private readonly HttpClient _http;
    private readonly string     _seedSession = Guid.NewGuid().ToString("N");

    public DemoSeeder(HttpClient http) => _http = http;

    // =========================================================================
    // Entry point
    // =========================================================================

    public async Task<int> RunAsync()
    {
        Console.WriteLine();
        Console.WriteLine("[DEMO] Starting demo data seed...");

        var today = DateOnly.FromDateTime(DateTime.Now);

        // -- Phase 1: Tasks --------------------------------------------------
        Console.WriteLine("  [DEMO] Creating tasks...");

        var taskDefs  = BuildTaskDefinitions(today);
        var tasksFailed = 0;

        foreach (var def in taskDefs)
        {
            var resp = await ConverseAsync(_seedSession, def.Command);

            if (resp?.Success == true)
                Console.WriteLine($"    ✓ Created: {def.Display} ({def.Hint})");
            else
            {
                Console.WriteLine($"    ✗ Failed:  {def.Display}");
                tasksFailed++;
            }
        }

        await Task.Delay(300);
        var allTasks  = await GetTasksAsync();
        var demoTasks = allTasks.Where(task => task.ShortDescription.StartsWith("[DEMO]")).ToList();

        foreach (var def in taskDefs.Where(def => def.Complete))
        {
            var match = demoTasks.FirstOrDefault(task => task.ShortDescription == def.Title);
            if (match is null) continue;

            var resp = await ConverseAsync(_seedSession, $"/task complete {match.Id}");

            if (resp?.Success == true)
                Console.WriteLine($"  ✓ Completed: {def.Display}");
        }

        // Re-fetch after completions
        await Task.Delay(200);
        allTasks  = await GetTasksAsync();
        demoTasks = allTasks.Where(task => task.ShortDescription.StartsWith("[DEMO]")).ToList();

        // -- Phase 2: Journal entries ----------------------------------------
        Console.WriteLine();
        Console.WriteLine("  [DEMO] Creating journal entries...");

        var journalDefs = BuildJournalDefinitions(today);

        foreach (var def in journalDefs)
        {
            var resp = await ConverseAsync(_seedSession, def.Command);

            if (resp?.Success == true)
                Console.WriteLine($"    ✓ Created: {def.Label}");
            else
                Console.WriteLine($"    ✗ Failed:  {def.Label}");
        }

        await Task.Delay(300);
        var allEntries  = await GetJournalEntriesAsync();
        var demoEntries = allEntries.Where(entry => entry.Text.StartsWith("[DEMO]")).ToList();

        // -- Phase 3: Daily record -------------------------------------------
        Console.WriteLine();
        Console.WriteLine("  [DEMO] Creating daily record...");

        var createdRecord = await SeedDailyRecordAsync(today);

        // -- Phase 4: Conversations ------------------------------------------
        Console.WriteLine();
        Console.WriteLine("  [DEMO] Creating conversations...");

        var conversationIds = await SeedConversationsAsync();

        // -- Manifest --------------------------------------------------------
        var manifest = new SeedManifest
                       {
                           SeededAt        = DateTimeOffset.UtcNow
                         , SeedSessionId   = _seedSession
                         , TaskIds         = demoTasks.Select(task => task.Id).ToList()
                         , JournalEntryIds = demoEntries.Select(entry => entry.Id).ToList()
                         , ConversationIds = conversationIds
                         , HasDailyRecord  = createdRecord
                         , DailyRecordDate = today.ToString("yyyy-MM-dd")
                       };

        SaveManifest(manifest);

        // -- Summary ---------------------------------------------------------
        var active    = demoTasks.Count(task => task.CompletedAt is null);
        var completed = demoTasks.Count(task => task.CompletedAt is not null);

        Console.WriteLine();
        Console.WriteLine($"[DEMO] Demo data seeded. {demoTasks.Count} tasks ({active} active, {completed} completed), {demoEntries.Count} journal entries, {(createdRecord ? 1 : 0)} daily record, {conversationIds.Count} conversations created.");
        Console.WriteLine("[DEMO] Run with --reset to clean up.");

        return 0;
    }

    // =========================================================================
    // Task definitions
    // =========================================================================

    private static List<TaskDef> BuildTaskDefinitions(DateOnly today)
    {
        var yesterday = today.AddDays(-1).ToString("yyyy-MM-dd");
        var tomorrow  = today.AddDays(1).ToString("yyyy-MM-dd");
        var in3Days   = today.AddDays(3).ToString("yyyy-MM-dd");
        var nextWeek  = today.AddDays(7).ToString("yyyy-MM-dd");
        var todayStr  = today.ToString("yyyy-MM-dd");

        return
        [
            new TaskDef
            {
                Title    = "[DEMO] Prepare quarterly business review presentation"
              , DueDate  = yesterday
              , Priority = "High"
              , Important= true
              , Urgent   = true
              , Display  = "Prepare quarterly business review presentation"
              , Hint     = "due yesterday"
              , Complete = false
            }
          , new TaskDef
            {
                Title    = "[DEMO] Review and sign lease renewal"
              , DueDate  = in3Days
              , Priority = "High"
              , Important= true
              , Urgent   = false
              , Display  = "Review and sign lease renewal"
              , Hint     = $"due in 3 days ({in3Days})"
              , Complete = false
            }
          , new TaskDef
            {
                Title    = "[DEMO] Schedule annual checkup"
              , DueDate  = nextWeek
              , Priority = "Normal"
              , Important= true
              , Urgent   = false
              , Display  = "Schedule annual checkup"
              , Hint     = "due next week"
              , Complete = false
            }
          , new TaskDef
            {
                Title    = "[DEMO] Follow up with client on proposal"
              , DueDate  = tomorrow
              , Priority = "High"
              , Important= true
              , Urgent   = true
              , Display  = "Follow up with client on proposal"
              , Hint     = "due tomorrow"
              , Complete = false
            }
          , new TaskDef
            {
                Title    = "[DEMO] Buy groceries for dinner party"
              , DueDate  = todayStr
              , Priority = "Normal"
              , Important= false
              , Urgent   = true
              , Display  = "Buy groceries for dinner party"
              , Hint     = "due today"
              , Complete = false
            }
          , new TaskDef
            {
                Title    = "[DEMO] Research project management tools for Q3"
              , DueDate  = nextWeek
              , Priority = "Normal"
              , Important= true
              , Urgent   = false
              , Display  = "Research project management tools for Q3"
              , Hint     = "due next week"
              , Complete = false
            }
          , new TaskDef
            {
                Title    = "[DEMO] Review team sprint velocity and capacity"
              , DueDate  = null
              , Priority = "Normal"
              , Important= true
              , Urgent   = false
              , Display  = "Review team sprint velocity and capacity"
              , Hint     = "no due date"
              , Complete = false
            }
          , new TaskDef
            {
                Title    = "[DEMO] Set up quarterly goals document"
              , DueDate  = null
              , Priority = "Normal"
              , Important= true
              , Urgent   = false
              , Display  = "Set up quarterly goals document"
              , Hint     = "completed"
              , Complete = true
            }
          , new TaskDef
            {
                Title    = "[DEMO] Call accountant about Q1 tax filing"
              , DueDate  = null
              , Priority = "Normal"
              , Important= true
              , Urgent   = false
              , Display  = "Call accountant about Q1 tax filing"
              , Hint     = "completed"
              , Complete = true
            }
          , new TaskDef
            {
                Title    = "[DEMO] Book conference travel and accommodations"
              , DueDate  = null
              , Priority = "Low"
              , Important= false
              , Urgent   = false
              , Display  = "Book conference travel and accommodations"
              , Hint     = "completed"
              , Complete = true
            }
          , new TaskDef
            {
                Title    = "[DEMO] Send weekly team status report"
              , DueDate  = null
              , Priority = "Low"
              , Important= false
              , Urgent   = false
              , Display  = "Send weekly team status report"
              , Hint     = "completed"
              , Complete = true
            }
        ];
    }

    // =========================================================================
    // Journal entry definitions
    // =========================================================================

    private static List<JournalDef> BuildJournalDefinitions(DateOnly today)
    {
        return
        [
            new JournalDef
            {
                Label   = $"{today.AddDays(-7):MMM d} — Week kickoff"
              , Command = JournalBlock(
                    text      : "[DEMO] Starting a fresh sprint and the backlog looks manageable for once. Called the accountant today and sorted the Q1 tax questions — that had been nagging at me for two weeks. Also finally booked the conference travel I had been avoiding. Big items cleared means I can focus on what actually matters this week."
                  , tags      : "planning,work,accomplishment"
                  , mood      : "Productive"
                  , moodScore : 4
                )
            }
          , new JournalDef
            {
                Label   = $"{today.AddDays(-6):MMM d} — Weekend reset"
              , Command = JournalBlock(
                    text      : "[DEMO] Proper weekend for the first time in a while. Did not look at email after Friday noon. Took a long walk Saturday morning which always resets my thinking. Had a useful idea about the presentation structure while completely offline — sometimes the best work happens when you step away."
                  , tags      : "personal,weekend,rest"
                  , mood      : "Refreshed"
                  , moodScore : 5
                )
            }
          , new JournalDef
            {
                Label   = $"{today.AddDays(-5):MMM d} — Monday intention"
              , Command = JournalBlock(
                    text      : "[DEMO] Monday morning and feeling the energy of a fresh week. The quarterly review is coming up fast and there is a lot to organize. Today I am blocking two hours for the presentation deck and making sure key stakeholders get their preview copies before Friday. One priority at a time."
                  , tags      : "reflection,monday,work"
                  , mood      : "Energized"
                  , moodScore : 4
                )
            }
          , new JournalDef
            {
                Label   = $"{today.AddDays(-3):MMM d} — Mid-week check-in"
              , Command = JournalBlock(
                    text      : "[DEMO] Hitting Wednesday and the week is flying. Got through most planned tasks but the lease renewal slipped to tomorrow — it takes longer than expected when you actually read the whole document. Team meeting went well and everyone seems aligned on Q3 priorities. Feeling stretched but manageable."
                  , tags      : "work,progress,reflection"
                  , mood      : "Focused"
                  , moodScore : 3
                )
            }
          , new JournalDef
            {
                Label   = $"{today.AddDays(-2):MMM d} — Pressure building"
              , Command = JournalBlock(
                    text      : "[DEMO] Feeling the pressure today. Three deadlines converging at once and I had to make triage calls. Pushed the annual checkup scheduling to next week — health admin can wait a few days. Client proposal follow-up is the real priority. Good conversation with the team about workload balance and glad I raised it."
                  , tags      : "stress,priorities,work"
                  , mood      : "Stressed"
                  , moodScore : 2
                )
            }
          , new JournalDef
            {
                Label   = $"{today.AddDays(-1):MMM d} — Turning point"
              , Command = JournalBlock(
                    text      : "[DEMO] Much better day than yesterday. Got the client proposal polished and sent — that was the big one hanging over me. Also cleared a bunch of smaller items that had been sitting in my queue. Taking an hour this evening just to decompress. Dinner with friends this weekend is coming at exactly the right time."
                  , tags      : "progress,work,personal"
                  , mood      : "Relieved"
                  , moodScore : 4
                )
            }
          , new JournalDef
            {
                Label   = $"{today:MMM d} — Morning reflection"
              , Command = JournalBlock(
                    text      : "[DEMO] Starting today with more clarity than I have had all week. Slept well and had time for coffee before the first notification hit. The QBR is coming up and I feel prepared. Main focus today: final presentation review, following up on the lease renewal, and getting the dinner party groceries sorted. One thing at a time."
                  , tags      : "morning,reflection,today"
                  , mood      : "Calm"
                  , moodScore : 4
                )
            }
        ];
    }

    private static string JournalBlock(string text, string tags, string mood, int moodScore)
        => string.Join("\n", "Journal", text, $"Tags: {tags}", $"Mood: {mood}", $"MoodScore: {moodScore}");

    // =========================================================================
    // Daily record
    // =========================================================================

    private async Task<bool> SeedDailyRecordAsync(DateOnly today)
    {
        var checkExisting = await _http.GetAsync("api/daily/today");

        if (checkExisting.IsSuccessStatusCode)
        {
            Console.WriteLine($"  ○ Skipped: daily record for {today:yyyy-MM-dd} already exists");
            return false;
        }

        var planCmd = string.Join("\n",
            "Plan: [DEMO] Focusing on the QBR presentation and finishing the week strong. Three clear priorities for today.",
            "Mood: Focused",
            "MoodScore: 4");

        var planResp = await ConverseAsync(_seedSession, planCmd);

        if (planResp?.Success != true)
        {
            Console.WriteLine("  ✗ Failed: daily record (open)");
            return false;
        }

        Console.WriteLine($"  ✓ Opened: daily record for {today:yyyy-MM-dd}");

        var checkCmd = string.Join("\n",
            "Check: [DEMO] Solid morning session. Completed the first pass on the presentation. Client reply drafted and ready to send. Unexpected call came in but resolved it well.",
            "Mood: On Track",
            "MoodScore: 3");

        var checkResp = await ConverseAsync(_seedSession, checkCmd);

        if (checkResp?.Success == true)
            Console.WriteLine("  ✓ Added: mid-day checkpoint");

        return true;
    }

    // =========================================================================
    // Conversations
    // =========================================================================

    private async Task<List<string>> SeedConversationsAsync()
    {
        var ids = new List<string>();

        // Conversation 1: Task review
        var session1 = Guid.NewGuid().ToString("N");
        await ConverseAsync(session1, "Show my tasks", fastPath: true);
        await ConverseAsync(session1, "Analyze my tasks", fastPath: true);
        await RenameConversationAsync(session1, "Task Review — Q2 Focus");
        ids.Add(session1);
        Console.WriteLine("  ✓ Created: \"Task Review — Q2 Focus\"");

        // Conversation 2: Daily brief
        var session2 = Guid.NewGuid().ToString("N");
        await ConverseAsync(session2, "Show my tasks", fastPath: true);
        await ConverseAsync(session2, "Show overdue tasks", fastPath: true);
        await RenameConversationAsync(session2, "Morning Brief");
        ids.Add(session2);
        Console.WriteLine("  ✓ Created: \"Morning Brief\"");

        // Conversation 3: Journal review
        var session3 = Guid.NewGuid().ToString("N");
        await ConverseAsync(session3, "/journal list", fastPath: true);
        await RenameConversationAsync(session3, "Journal Review — This Week");
        ids.Add(session3);
        Console.WriteLine("  ✓ Created: \"Journal Review — This Week\"");

        return ids;
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

    private async Task RenameConversationAsync(string id, string name)
    {
        try
        {
            await _http.PutAsJsonAsync($"api/conversation/{id}/name", new { Name = name }, JsonOptions);
        }
        catch { /* best effort */ }
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

    internal static void SaveManifest(SeedManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SeedManifest.FileName, json);
        Console.WriteLine($"  ✓ Manifest saved → {SeedManifest.FileName}");
    }
}

// =============================================================================
// Supporting types
// =============================================================================

internal sealed class TaskDef
{
    public required string  Title     { get; init; }
    public          string? DueDate   { get; init; }
    public required string  Priority  { get; init; }
    public required bool    Important { get; init; }
    public required bool    Urgent    { get; init; }
    public required string  Display   { get; init; }
    public required string  Hint      { get; init; }
    public required bool    Complete  { get; init; }

    public string Command
    {
        get
        {
            var lines = new List<string> { $"task: {Title}" };
            if (DueDate is not null) lines.Add($"DueDate: {DueDate}");
            lines.Add($"priority: {Priority}");
            lines.Add($"isImportant: {Important}");
            lines.Add($"isUrgent: {Urgent}");
            return string.Join("\n", lines);
        }
    }
}

internal sealed class JournalDef
{
    public required string Label   { get; init; }
    public required string Command { get; init; }
}

internal sealed class ConverseRequest
{
    public string  SessionId { get; init; } = string.Empty;
    public string? Input     { get; init; }
    public bool    FastPath  { get; init; }
    public bool    Streaming { get; init; }
}

internal sealed class ConverseResponse
{
    public bool    Success         { get; init; }
    public string? Message         { get; init; }
    public string? ExecutionResult { get; init; }
    public bool    WasFastPath     { get; init; }
}

internal sealed class ApiTaskItem
{
    public string  Id               { get; init; } = string.Empty;
    public string  ShortDescription { get; init; } = string.Empty;
    public string? CompletedAt      { get; init; }
    public string? DueDate          { get; init; }
}

internal sealed class ApiJournalEntry
{
    public string Id   { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
}

public sealed class SeedManifest
{
    public const string FileName = "demo-seed-manifest.json";

    public DateTimeOffset SeededAt        { get; set; }
    public string         SeedSessionId   { get; set; } = string.Empty;
    public List<string>   TaskIds         { get; set; } = [];
    public List<string>   JournalEntryIds { get; set; } = [];
    public List<string>   ConversationIds { get; set; } = [];
    public bool           HasDailyRecord  { get; set; }
    public string?        DailyRecordDate { get; set; }
}
