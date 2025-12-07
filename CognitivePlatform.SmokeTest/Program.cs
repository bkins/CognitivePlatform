using System.Net.Http.Json;
using System.Text.Json;
using System.Text
    .Encodings
    .Web;
using CognitivePlatform.SmokeTest.Helpers;

Console.OutputEncoding = System.Text.Encoding.UTF8;

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

    // if (input == "spin")
    // {
    //     for (int i = 0; i <= ConsoleSpinner.NumberOfSpinnerStyles-1; i++)
    //     {
    //         using (new ConsoleSpinner($"API Call {i + 1}", (SpinnerStyle)i))
    //         {
    //             await Task.Delay(3000);
    //         }
    //         Console.WriteLine($"Call {i + 1} complete! ({string.Join(',',ConsoleSpinner.SpinnerStyles[i])})");
    //     }
    //     
    //     continue;
    // }
    
    if (string.IsNullOrWhiteSpace(input))
        continue;

    var request = new
                  {
                      sessionId = sessionId
                    , input     = input
                  };
    
    HttpResponseMessage response;
    using (new ConsoleSpinner("Thinking", SpinnerStyle.WaveText))
    {
        response = await http.PostAsJsonAsync("api/conversation/converse"
                                            , request);
    }

    var rawJson = await response.Content.ReadAsStringAsync();
    
    // Try to pretty-print JSON
    string pretty;

    try
    {
        using var doc = JsonDocument.Parse(rawJson);
        pretty = JsonSerializer.Serialize(doc
                                        , new JsonSerializerOptions
                                          {
                                              WriteIndented = true
                                            , Encoder       = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
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

