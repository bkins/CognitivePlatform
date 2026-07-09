using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Registry.Domains;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Actions;

/// <summary>
/// Session-scoped LLM model and provider actions.
///
/// SetContext and SetCatalog must be called once per request by the orchestrator
/// before any action in this class can be invoked.
///
/// Model and provider preference are stored as an immutable
/// <see cref="LlmSession"/> snapshot on <see cref="ConversationContext"/>,
/// swapped atomically so that <see cref="ILlmRouter"/> can never observe a
/// torn (provider, model) pair during a mid-session switch.
/// </summary>
[Domain(typeof(SystemDomain))]
public static class LlmActions
{
    // Retained for backwards compatibility with existing test fixtures and
    // any external telemetry contract. The session source of truth is now
    // ConversationContext.CurrentLlmSession; do not read these from production code.
    public const string SessionModelKey    = "session_model";
    public const string SessionProviderKey = "session_provider";

    private static readonly System.Threading.AsyncLocal<ConversationContext?> _contextLocal = new();
    private static ConversationContext? _context
    {
        get => _contextLocal.Value;
        set => _contextLocal.Value = value;
    }

    private static readonly System.Threading.AsyncLocal<LlmModelCatalog?> _catalogLocal = new();
    private static LlmModelCatalog? _catalog
    {
        get => _catalogLocal.Value;
        set => _catalogLocal.Value = value;
    }

    private static readonly System.Threading.AsyncLocal<LlmProviderDefaults?> _providerDefaultsLocal = new();
    private static LlmProviderDefaults? _providerDefaults
    {
        get => _providerDefaultsLocal.Value;
        set => _providerDefaultsLocal.Value = value;
    }

    public static void SetContext (ConversationContext context) => _context = context;
    public static void SetCatalog (LlmModelCatalog     catalog) => _catalog = catalog;

    public static void SetProviderDefaults (LlmProviderDefaults defaults) =>
        _providerDefaults = defaults;

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
            _context.SetLlmModel(model.Trim());
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

        _context.SetLlmModel(match.Name);
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

        var currentModel = _context?.CurrentLlmSession.Model;

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
    // SetProvider / ListProviders — DEFERRED #10
    // ----------------------------------------------------------------

    [NaturalLanguageAction(
        Description = "Switches the LLM provider for the remainder of this session "
                    + "(Groq, Gemini, Ollama, OpenRouter, or Cerebras). "
                    + "The session's model resets to that provider's configured default; "
                    + "use 'set model X' afterwards to pick something non-default."
      , Examples =
        [
                "Switch to OpenRouter"
              , "Use provider Groq"
              , "Set provider to Gemini"
        ]
      , Category = "interpreter"
    )]
    public static string SetProvider (string provider)
    {
        if (_context is null)
            return "Session context is not available. SetContext was not called.";

        if (provider.HasNoValue())
            return "Please provide a provider name. Say 'list providers' to see what is available.";

        var trimmed = provider.Trim();

        if (Enum.TryParse<LlmProvider>(trimmed, ignoreCase: true, out var parsed).Not())
        {
            var available = string.Join(", ", Enum.GetNames<LlmProvider>());
            return $"Unknown provider '{trimmed}'. Available providers: {available}.";
        }

        var defaultModel = _providerDefaults?.For(parsed);

        if (string.IsNullOrWhiteSpace(defaultModel))
        {
            return $"Provider '{parsed}' has no default model configured. "
                 + $"Set one under 'Llm:Defaults:{parsed}' in configuration.";
        }

        _context.SetLlmSession(parsed, defaultModel);

        return $"Switched to {parsed} (default model: {defaultModel}). "
             + "Use 'set model X' to pick a different model.";
    }

    [NaturalLanguageAction(
        Description = "Lists the LLM providers known to the system, marks the active session "
                    + "provider, and notes which ones have a default model configured."
      , Examples =
        [
                "List providers"
              , "What providers are available?"
              , "Show me the available LLM providers"
        ]
      , Category = "interpreter"
    )]
    public static string ListProviders()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Available providers:");

        var currentProvider = _context?.CurrentLlmSession.Provider;

        foreach (var provider in Enum.GetValues<LlmProvider>())
        {
            var defaultModel = _providerDefaults?.For(provider);
            var isCurrent    = provider.ToString().Equals(currentProvider
                                                        , StringComparison.OrdinalIgnoreCase);

            var marker = isCurrent ? " (current)" : string.Empty;

            if (defaultModel.HasValue())
                sb.AppendLine($"  {provider}{marker} — default model: {defaultModel}");
            else
                sb.AppendLine($"  {provider}{marker} — not configured");
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
