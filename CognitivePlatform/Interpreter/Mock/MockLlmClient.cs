using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Interpreter;

public class MockLlmClient : ILlmClient
{
    public Task<LlmResponse> SendAsync(string prompt, string? model = null, CancellationToken cancellationToken = default)
    {
        var content = ResolveResponse(prompt);
        return Task.FromResult(new LlmResponse
        {
            Content = content,
            Usage = new LlmUsageInfo
            {
                PromptTokens = 10,
                CompletionTokens = 20,
                TotalTokens = 30
            },
            RateLimits = LlmRateLimitSnapshot.Empty
        });
    }

    public async IAsyncEnumerable<string> StreamAsync(string prompt, string? model = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = ResolveResponse(prompt);
        var chunks = response.Split(' ');
        foreach (var chunk in chunks)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return chunk + " ";
            await Task.Delay(10, cancellationToken);
        }
    }

    public Task<LlmModelProbeResult> ProbeAsync(string model, CancellationToken ct = default)
    {
        return Task.FromResult(new LlmModelProbeResult(model, true));
    }

    private string ResolveResponse(string prompt)
    {
        if (prompt.Contains("how are my tasks doing?"))
        {
            return """
            {
              "actionName": "ListTasks",
              "reason": "The user wants to check how their tasks are doing.",
              "parameters": {}
            }
            """;
        }
        
        if (prompt.Contains("give me a daily brief"))
        {
            return """
            {
              "actionName": "GetDailyBrief",
              "reason": "The user requested their daily brief.",
              "parameters": {}
            }
            """;
        }
        
        if (prompt.Contains("use knowledge domain HumanResources"))
        {
            return """
            {
              "actionName": "UseKnowledgeDomain",
              "reason": "The user wants to activate the HumanResources knowledge domain.",
              "parameters": {
                "domainName": "HumanResources"
              }
            }
            """;
        }

        if (prompt.Contains("breakfast", StringComparison.OrdinalIgnoreCase) || prompt.Contains("scrambled eggs", StringComparison.OrdinalIgnoreCase))
        {
            return """
            {
              "actionName": "LogMeal",
              "reason": "The user wants to log breakfast.",
              "parameters": {
                "meal": {
                  "mealType": "Breakfast",
                  "foods": [
                    { "name": "scrambled eggs", "quantity": 2, "unit": "large" },
                    { "name": "toast", "quantity": 1, "unit": "slice" }
                  ]
                }
              }
            }
            """;
        }

        if (prompt.Contains("lunch", StringComparison.OrdinalIgnoreCase) || prompt.Contains("turkey sandwich", StringComparison.OrdinalIgnoreCase))
        {
            return """
            {
              "actionName": "LogMeal",
              "reason": "The user wants to log lunch.",
              "parameters": {
                "meal": {
                  "mealType": "Lunch",
                  "foods": [
                    { "name": "turkey sandwich", "quantity": 1, "unit": "serving" },
                    { "name": "chips", "quantity": 1, "unit": "bag" }
                  ]
                }
              }
            }
            """;
        }

        if (prompt.Contains("dinner", StringComparison.OrdinalIgnoreCase) || prompt.Contains("grilled salmon", StringComparison.OrdinalIgnoreCase))
        {
            return """
            {
              "actionName": "LogMeal",
              "reason": "The user wants to log dinner.",
              "parameters": {
                "meal": {
                  "mealType": "Dinner",
                  "foods": [
                    { "name": "grilled salmon", "quantity": 1, "unit": "fillet" },
                    { "name": "rice", "quantity": 1, "unit": "cup" }
                  ]
                }
              }
            }
            """;
        }

        if (prompt.Contains("what did i eat", StringComparison.OrdinalIgnoreCase) || prompt.Contains("list meals", StringComparison.OrdinalIgnoreCase))
        {
            return """
            {
              "actionName": "ListMeals",
              "reason": "The user is asking for their logged meals.",
              "parameters": {}
            }
            """;
        }

        // Default chit-chat fallback response
        return """
        {
          "actionName": "ChitChat",
          "reason": "The input is a general conversational remark.",
          "parameters": {
            "text": "Hello, I am the Mock LLM. I can simulate responses for tests."
          }
        }
        """;
    }
}
