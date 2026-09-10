using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Conversation;

namespace CognitivePlatform.Tests;

public sealed class TurnTransparencySummaryTests
{
    [Fact]
    public void Build_UsesSafeFactsForAnExecutedInterpreterAction()
    {
        var response = new ConverseResponse
                       {
                               Success                      = true
                             , Provider                     = "Groq"
                             , Model                        = "safe-model"
                             , PendingMemoryCount           = 1
                             , IsConfirmationRequired       = true
                             , Insights                     = [new()]
                       };

        var items = TurnTransparencySummary.Build(response, TurnPath.Interpreter, "tasks.create");

        Assert.Contains(items, item => item is { Label: "Model routing", Detail: "Groq / safe-model handled this turn." });
        Assert.Contains(items, item => item is { Label: "Engine decision", Detail: "The engine selected the tasks.create action." });
        Assert.Contains(items, item => item.Label == "Safety gate");
        Assert.Contains(items, item => item.Label == "Memory");
        Assert.Contains(items, item => item.Label == "Insights");
    }

    [Fact]
    public void Build_ExplainsFastPathWithoutClaimingModelUse()
    {
        var items = TurnTransparencySummary.Build(new ConverseResponse { WasFastPath = true }, TurnPath.FastPath, "tasks.list");

        var decision = Assert.Single(items, item => item.Label == "Decision path");
        Assert.Contains("no model was used", decision.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(items, item => item.Label == "Model routing");
    }
}
