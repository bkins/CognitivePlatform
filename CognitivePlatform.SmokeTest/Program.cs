using System.Net.Http.Json;
using System.Text.Json;

Console.WriteLine("Phase 2 Smoke Tester\n---------------------");

var http = new HttpClient
           {
               BaseAddress = new Uri("http://localhost:5272"),
               Timeout     = TimeSpan.FromSeconds(500)
           };

// Create or reuse a session ID
var sessionId = Guid.NewGuid().ToString();

Console.WriteLine($"Using session: {sessionId}");

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;

    var request = new
                  {
                      sessionId = sessionId,
                      input     = input
                  };

    var response = await http.PostAsJsonAsync(
        "api/conversation/converse",
        request);

    var rawJson = await response.Content.ReadAsStringAsync();

    // Try to pretty-print JSON
    string pretty;

    try
    {
        using var doc = JsonDocument.Parse(rawJson);
        pretty = JsonSerializer.Serialize(
            doc,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder       = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
    }
    catch
    {
        pretty = rawJson; // fallback if response isn't valid JSON
    }


    Console.WriteLine("\n[Response]");
    Console.WriteLine(pretty);
    Console.WriteLine();
}


// using System.Net.Http.Json;
//
// Console.WriteLine("Phase 2 Smoke Tester\n---------------------");
//
// var http = new HttpClient
//            {
//                BaseAddress = new Uri("http://localhost:5272"),
//                Timeout     = TimeSpan.FromSeconds(500)
//            };
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
//                       sessionId = sessionId,
//                       input     = input
//                   };
//
//     var response = await http.PostAsJsonAsync(
//         "api/conversation/converse",
//         request);
//
//     var result = await response.Content.ReadAsStringAsync();
//
//     Console.WriteLine($"\n[Response]\n{result}\n");
// }