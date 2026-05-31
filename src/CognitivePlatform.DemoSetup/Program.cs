using CognitivePlatform.DemoSetup;

const string baseUrl = "http://localhost:5273";

var mode = args.Contains("--reset") ? "reset" : "seed";

using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

try
{
    if (mode == "reset")
    {
        var resetter = new DemoReset(http);
        return await resetter.RunAsync();
    }

    var seeder = new DemoSeeder(http);
    return await seeder.RunAsync();
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"[DEMO] ERROR: Cannot connect to API at {baseUrl}");
    Console.Error.WriteLine("[DEMO] Make sure the CP API is running first.");
    Console.Error.WriteLine($"[DEMO] Details: {ex.Message}");
    return 1;
}
