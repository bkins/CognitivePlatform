using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Encodings.Web;
using CognitivePlatform.SmokeTest.Helpers;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("Cognitive Platform Smoke Tester");
Console.WriteLine("-------------------------------");

var http = new HttpClient
{
    BaseAddress = new Uri("http://localhost:5272"),
    Timeout     = TimeSpan.FromSeconds(500)
};

// ---------- WAIT FOR API ----------

using (new ConsoleSpinner("Waiting for API", SpinnerStyle.WaveText))
{
    await WaitForApiReady(http, TimeSpan.FromSeconds(500));
}

// ---------- INTERACTIVE LOOP ----------

var sessionId = Guid.NewGuid().ToString();
Console.WriteLine($"Using session: {sessionId}");

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;

    var isVerbose = input.EndsWith("[Verbose]", StringComparison.OrdinalIgnoreCase);

    if (isVerbose)
    {
        input = input[..^"[Verbose]".Length].TrimEnd();
    }


    var request = new
    {
        sessionId
       , input
    };

    HttpResponseMessage response;

    using (new ConsoleSpinner("Thinking", SpinnerStyle.WaveText))
    {
        response = await http.PostAsJsonAsync("api/conversation/converse", request);
    }

    var rawJson = await response.Content.ReadAsStringAsync();

    using var doc = JsonDocument.Parse(rawJson);

    
    Console.WriteLine("\n[Response]");
    
    if (isVerbose)
    {
        Console.WriteLine(PrettyPrintJson(rawJson));
        Console.WriteLine();
        continue;
    }
    
    if (TryRenderHumanFriendlyListActions(rawJson))
    {
        Console.WriteLine();
        continue;
    }
    
    if (TryRenderMessage(doc))
    {
        continue;
    }
    
    Console.WriteLine(PrettyPrintJson(rawJson));
    Console.WriteLine();
}

// ---------- helpers ----------
static bool TryRenderHumanFriendlyListActions(string rawJson)
{
    try
    {
        using var doc = JsonDocument.Parse(rawJson);

        // We only care about successful ListActions executions
        if (!doc.RootElement.TryGetProperty("executionResult", out var exec))
            return false;

        if (!exec.TryGetProperty("metadata", out var metadata))
            return false;

        if (metadata.ValueKind != JsonValueKind.Array)
            return false;

        RenderActionCatalog(metadata);
        return true;
    }
    catch
    {
        return false;
    }
}

static void RenderActionCatalog(JsonElement metadata)
{
    Console.WriteLine("I can help you with:\n");

    var groups = metadata
                 .EnumerateArray()
                 .GroupBy(a => a.GetProperty("category").GetString() ?? "General",
                          StringComparer.OrdinalIgnoreCase);

    foreach (var group in groups.OrderBy(g => g.Key))
    {
        Console.WriteLine(NormalizeCategoryName(group.Key));

        foreach (var action in group.OrderBy(a => a.GetProperty("name").GetString()))
        {
            var name        = action.GetProperty("name").GetString()!;
            var description = action.GetProperty("description").GetString();

            Console.WriteLine($" • {HumanizeActionName(name)}");

            if (!string.IsNullOrWhiteSpace(description))
                Console.WriteLine($"   {description}");
        }

        Console.WriteLine();
    }

    Console.WriteLine("Tip: type \"describe <action>\" to learn more about a command.");
    Console.WriteLine("Tip: type \"list actions verbose\" to see technical details.");
}

static string NormalizeCategoryName(string raw)
{
    return raw switch
    {
            "General"     => "General",
            "Debug"       => "Debug",
            "Interpreter" => "Meta",
            _             => raw
    };
}

static string HumanizeActionName(string name)
{
    // AddTask → add task
    return string.Concat(
        name.Select((c, i) =>
                            i > 0 && char.IsUpper(c)
                                    ? " " + char.ToLowerInvariant(c)
                                    : char.ToLowerInvariant(c).ToString()
        )
    );
}

static async Task WaitForApiReady(HttpClient http, TimeSpan timeout)
{
    var start = DateTime.UtcNow;

    while (DateTime.UtcNow - start < timeout)
    {
        try
        {
            var resp = await http.GetAsync("/health/ready");

            if (resp.IsSuccessStatusCode)
                return;
        }
        catch
        {
            // API not listening yet
        }

        await Task.Delay(500);
    }

    throw new Exception("API did not become ready within timeout.");
}

static string PrettyPrintJson(string raw)
{
    try
    {
        using var doc = JsonDocument.Parse(raw);
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder       = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }
    catch
    {
        return raw;
    }
}

static bool TryRenderMessage(JsonDocument doc)
{
    if (!doc.RootElement.TryGetProperty("message", out var message))
        return false;

    if (message.ValueKind != JsonValueKind.String)
        return false;

    Console.WriteLine();
    Console.WriteLine(message.GetString());
    Console.WriteLine();
    return true;
}

// using System.Net.Http.Json;
// using System.Text.Json;
// using System.Text
//     .Encodings
//     .Web;
// using CognitivePlatform.SmokeTest.Helpers;
//
// Console.OutputEncoding = System.Text.Encoding.UTF8;
//
// Console.WriteLine("Cognitive Platform Smoke Tester\n---------------------");
//
// var http = new HttpClient
//            {
//                BaseAddress = new Uri("http://localhost:5272"),
//                Timeout     = TimeSpan.FromSeconds(500)
//            };
// var warmUpRequest = new
//                     {
//                             sessionId = "0"
//                           , input     = "just warming things up"
//                           , model     = "phi-3:mini"
//                     };
//     
// HttpResponseMessage warmUpResponse;
// using (new ConsoleSpinner("Warming Up", SpinnerStyle.WaveText))
// {
//     _ = await http.GetAsync("/health/ready");
//     // warmUpResponse = await http.PostAsJsonAsync("api/conversation/converse"
//     //                                           , warmUpRequest);
// }
//
// // Create or reuse a session ID
// var sessionId = Guid.NewGuid().ToString();
//
// Console.WriteLine($"Using session: {sessionId}");
//
// while (true)
// {
//     Console.Write("> ");
//     var input = Console.ReadLine();
//
//     if (string.IsNullOrWhiteSpace(input))
//         continue;
//
//     var request = new
//                   {
//                       sessionId = sessionId
//                     , input     = input
//                   };
//     
//     HttpResponseMessage response;
//     using (new ConsoleSpinner("Thinking", SpinnerStyle.WaveText))
//     {
//         response = await http.PostAsJsonAsync("api/conversation/converse"
//                                             , request);
//     }
//     
//     var rawJson = await response.Content.ReadAsStringAsync();
//     
//     // Try to pretty-print JSON
//     string pretty;
//
//     try
//     {
//         using var doc = JsonDocument.Parse(rawJson);
//         pretty = JsonSerializer.Serialize(doc
//                                         , new JsonSerializerOptions
//                                           {
//                                               WriteIndented = true
//                                             , Encoder       = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
//                                           });
//     }
//     catch
//     {
//         pretty = rawJson; // fallback if response isn't valid JSON
//     }
//     
//     Console.WriteLine("\n[Response]");
//     Console.WriteLine(pretty);
//     Console.WriteLine();
// }
//
// async Task<string> GetResponse(string input, string sessionId, string spinnerMessage)
// {
//     var warmUpRequest = new
//                         {
//                                 sessionId = sessionId
//                               , input     = input
//                         };
//     
//     HttpResponseMessage warmUpResponse;
//     using (new ConsoleSpinner(spinnerMessage, SpinnerStyle.WaveText))
//     {
//         warmUpResponse = await http.PostAsJsonAsync("api/conversation/converse"
//                                                   , warmUpRequest);
//     }
//
//     return await warmUpResponse.Content.ReadAsStringAsync();
// }
