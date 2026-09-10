using CognitivePlatform.Api.Contracts;

namespace CognitivePlatform.Api.Conversation;

public static class TurnTransparencySummary
{
    public static IReadOnlyList<TransparencyItem> Build( ConverseResponse response
                                                        , TurnPath         path
                                                        , string?          actionName )
    {
        var items = new List<TransparencyItem>();

        items.Add(path == TurnPath.FastPath || response.WasFastPath
                      ? new TransparencyItem("Decision path", "A direct, deterministic rule handled this turn; no model was used.")
                      : new TransparencyItem("Model routing", $"{response.Provider ?? "Configured provider"} / {response.Model ?? "selected model"} handled this turn."));

        if (actionName is not null)
            items.Add(new TransparencyItem("Engine decision", $"The engine selected the {actionName} action."));
        else if (path == TurnPath.Clarification)
            items.Add(new TransparencyItem("Clarification", "More information is required before the engine can execute an action."));

        if (response.IsConfirmationRequired)
            items.Add(new TransparencyItem("Safety gate", "Confirmation is required before this action can execute."));

        items.Add(response.Success
                      ? new TransparencyItem("Outcome", actionName is null ? "A response was generated; no action was executed." : "The selected action completed successfully.")
                      : new TransparencyItem("Outcome", "The requested work did not complete; no unconfirmed follow-up action was taken."));

        if (response.PendingMemoryCount > 0)
            items.Add(new TransparencyItem("Memory", $"{response.PendingMemoryCount} memory assertion(s) remain awaiting confirmation."));

        if (response.Insights.Count > 0)
            items.Add(new TransparencyItem("Insights", $"{response.Insights.Count} insight(s) were made available with this response."));

        if (response.ModelNotice is not null)
            items.Add(new TransparencyItem("Model routing note", response.ModelNotice));

        return items;
    }
}
