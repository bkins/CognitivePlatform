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
