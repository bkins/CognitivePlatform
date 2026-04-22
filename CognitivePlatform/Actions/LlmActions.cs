using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Conversation;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Actions;

/// <summary>
/// Session-scoped LLM model actions.
///
/// SetContext and SetCatalog must be called once per request by the orchestrator
/// before any action in this class can be invoked.
///
/// Model preference is stored in context.Metadata["session_model"] so it
/// survives across conversation turns. The orchestrator reads this key and
/// propagates it into the per-request "model" slot used by LlmInterpreter.
/// </summary>
public static class LlmActions
{
    public const string SessionModelKey = "session_model";

    private static ConversationContext? _context;
    private static LlmModelCatalog?     _catalog;

    public static void SetContext (ConversationContext context) => _context = context;
    public static void SetCatalog (LlmModelCatalog    catalog) => _catalog = catalog;

    [NaturalLanguageAction(
        Description = "Sets the LLM model to use for the remainder of this session. "
                    + "The model must be known and available; use ListModels to see options."
      , Examples =
        [
                "Use model llama-3.3-70b-versatile"
              , "Switch to gpt-4o-mini"
              , "Set model to llama3.1-8b"
        ]
      , Category = "interpreter"
    )]
    public static string SetModel (string model)
    {
        if (_context is null)
            return "Session context is not available. SetContext was not called.";

        if (model.HasNoValue())
            return "Please provide a model name. Say 'list models' to see what is available.";

        if (_catalog is null)
        {
            _context.Metadata[SessionModelKey] = model.Trim();
            return $"Model set to '{model.Trim()}'. (Model catalog unavailable — no validation performed.)";
        }

        var trimmed = model.Trim();
        var match   = _catalog.AvailableModels
                              .FirstOrDefault(info => info.Name.Equals(trimmed
                                                                     , StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var available = BuildModelList();
            return $"Unknown model '{trimmed}'. Available models:\n{available}";
        }

        if (match.IsUsable.Not())
        {
            var reason = match.FailureReason.HasValue()
                                 ? $" ({match.FailureReason})"
                                 : string.Empty;
            return $"Model '{match.Name}' is not usable on this system{reason}. "
                 + $"Say 'list models' to see usable options.";
        }

        _context.Metadata[SessionModelKey] = match.Name;
        return $"Model set to '{match.Name}' for this session.";
    }

    [NaturalLanguageAction(
        Description = "Lists the LLM models that are available and usable in the current session."
      , Examples =
        [
                "List models"
              , "What models are available?"
              , "Show me the available LLM models"
        ]
      , Category = "interpreter"
    )]
    public static string ListModels()
    {
        if (_catalog is null)
            return "Model catalog is not available.";

        if (_catalog.AvailableModels.Count == 0)
            return "No models have been probed yet. The catalog is empty.";

        var sb = new StringBuilder();
        sb.AppendLine("Available models:");

        var currentModel = _context?.Metadata.GetValueOrDefault(SessionModelKey);

        foreach (var info in _catalog.AvailableModels)
        {
            var marker = info.Name.Equals(currentModel, StringComparison.OrdinalIgnoreCase)
                                 ? " (current)"
                                 : string.Empty;

            if (info.IsUsable)
                sb.AppendLine($"  {info.Name}{marker}");
            else
                sb.AppendLine($"  {info.Name} [unavailable: {info.FailureReason ?? "unknown"}]");
        }

        return sb.ToString().TrimEnd();
    }

    // ----------------------------------------------------------------
    // Internal helpers
    // ----------------------------------------------------------------

    private static string BuildModelList()
    {
        if (_catalog is null || _catalog.AvailableModels.Count == 0)
            return "  (no models probed)";

        var sb = new StringBuilder();
        foreach (var info in _catalog.AvailableModels.Where(info => info.IsUsable))
            sb.AppendLine($"  {info.Name}");
        return sb.ToString().TrimEnd();
    }
}
