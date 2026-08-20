using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Knowledge.Models;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry.Domains;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Knowledge;

[Domain(typeof(Registry.Domains.KnowledgeDomain))]
public sealed class KnowledgeActions : ISessionAware
{
    private readonly IKnowledgeIngestionService _ingestionService;
    private readonly ILlmRouter _llmRouter;
    private ConversationContext? _context;

    public KnowledgeActions(IKnowledgeIngestionService ingestionService, ILlmRouter llmRouter)
    {
        _ingestionService = ingestionService ?? throw new ArgumentNullException(nameof(ingestionService));
        _llmRouter = llmRouter ?? throw new ArgumentNullException(nameof(llmRouter));
    }

    public void SetSessionContext(ConversationContext context)
    {
        _context = context;
    }

    [NaturalLanguageAction(
        Description = "Sets the active knowledge domain for the current conversation."
      , Examples = new[]
        {
            "use knowledge domain HumanResources"
          , "activate knowledge domain legal"
          , "use domain coding-guidelines"
        }
      , Category = "knowledge"
    )]
    public async Task<string> UseKnowledgeDomain(
        [NaturalLanguageParam(Description = "The name of the knowledge domain to activate.")]
        string domainName)
    {
        if (_context is null)
            return "No conversation context active.";

        if (domainName.HasNoValue())
            return "Please specify a domain name.";

        var domain = await _ingestionService.GetDomainAsync(domainName);
        if (domain is null)
            return $"Knowledge domain '{domainName}' does not exist.";

        _context.Metadata["active_knowledge_domain"] = domain.Name;
        return $"Now using knowledge domain '{domain.Name}' ({domain.Mode} mode).";
    }

    [NaturalLanguageAction(
        Description = "Clears the active knowledge domain, returning the assistant to normal conversation."
      , Examples = new[]
        {
            "clear knowledge domain"
          , "stop using knowledge domain"
          , "deactivate domain"
        }
      , Category = "knowledge"
    )]
    public string ClearKnowledgeDomain()
    {
        if (_context is null)
            return "No conversation context active.";

        if (_context.Metadata.TryRemove("active_knowledge_domain", out var domainName))
        {
            return $"Cleared active knowledge domain '{domainName}'.";
        }

        return "No active knowledge domain was set.";
    }

    [NaturalLanguageAction(
        Description = "Lists all registered knowledge domains and their modes."
      , Examples = new[]
        {
            "list knowledge domains"
          , "what domains are available?"
          , "show knowledge domains"
        }
      , Category = "knowledge"
    )]
    public async Task<string> ListKnowledgeDomains()
    {
        var domains = await _ingestionService.ListDomainsAsync();
        if (domains.Count == 0)
            return "No knowledge domains have been registered yet.";

        var sb = new StringBuilder();
        sb.AppendLine("Available knowledge domains:");
        foreach (var d in domains)
        {
            sb.AppendLine($"- {d.Name} ({d.Mode} mode): {d.Description}");
        }

        return sb.ToString().TrimEnd();
    }

    [NaturalLanguageAction(
        Description = "Queries the active knowledge domain directly for information using retrieved context."
      , Examples = new[]
        {
            "query knowledge about PTO"
          , "ask expert: how many days off do I get?"
          , "query knowledge about coding standards"
        }
      , Category = "knowledge"
    )]
    public async Task<string> QueryKnowledge(
        [NaturalLanguageParam(Description = "The question or query to search for.")]
        string query)
    {
        if (_context is null)
            return "No conversation context active.";

        if (query.HasNoValue())
            return "Please provide a query.";

        if (!_context.Metadata.TryGetValue("active_knowledge_domain", out var domainName) || domainName.HasNoValue())
            return "Please activate a knowledge domain first using 'use knowledge domain [name]'.";

        var domain = await _ingestionService.GetDomainAsync(domainName);
        if (domain is null)
            return $"Active knowledge domain '{domainName}' does not exist.";

        // Retrieve relevant context chunks
        var chunks = await _ingestionService.RetrieveContextAsync(domainName, query);

        if (chunks.Count == 0 && domain.Mode == KnowledgeDomainMode.Strict)
        {
            return "UNKNOWN";
        }

        // Build prompt and call LLM
        var prompt = await BuildGroundedPromptAsync(query, domain, chunks, _context, _ingestionService);
        var llmResponse = await _llmRouter.SendAsync(prompt, _context);

        return llmResponse.Content;
    }

    // Helper to construct RAG prompt
    public static async Task<string> BuildGroundedPromptAsync(
        string userMessage,
        Models.KnowledgeDomain domain,
        IReadOnlyList<Integrations.Embeddings.VectorSearchResult> chunks,
        ConversationContext context,
        IKnowledgeIngestionService ingestionService)
    {
        var template = await File.ReadAllTextAsync("Prompts/grounding.txt");

        // Format chunks with attribution
        var chunksBuilder = new StringBuilder();
        if (chunks.Count == 0)
        {
            chunksBuilder.AppendLine("(No matching source documents found.)");
        }
        else
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var docTitle = "Document";
                var docSource = "Unknown Source";

                // Load source metadata
                if (Guid.TryParse(chunk.Entry.ReferenceId, out var objId))
                {
                    var obj = await ingestionService.GetObjectAsync(domain.Name, objId);
                    if (obj is not null)
                    {
                        docTitle = obj.Title;
                        docSource = obj.Source;
                    }
                }

                chunksBuilder.AppendLine($"[{i + 1}] Source: {docTitle} (Origin: {docSource})");
                chunksBuilder.AppendLine($"Content: {chunk.Entry.Text}");
                chunksBuilder.AppendLine();
            }
        }

        // Policy text
        var policy = domain.Mode switch
        {
            KnowledgeDomainMode.Strict =>
                "You MUST answer the user's query ONLY using the RETRIEVED KNOWLEDGE DOMAIN data provided above. If the answer cannot be fully found in the provided retrieved data, respond ONLY with: 'UNKNOWN'. Do not use your own pre-trained knowledge or assume any facts.",
            KnowledgeDomainMode.Grounded =>
                "You should answer the user's query using the RETRIEVED KNOWLEDGE DOMAIN data. Prefer this data over your general knowledge. If the answer is not there, explain that the information is not in the source documents rather than guessing.",
            KnowledgeDomainMode.Advisory =>
                "The RETRIEVED KNOWLEDGE DOMAIN data is provided for your reference. Use it to inform your answer, but you may also draw on your general knowledge if needed.",
            _ => throw new ArgumentOutOfRangeException()
        };

        // Format history
        var historyBuilder = new StringBuilder();
        var historyTurns = context.Turns.TakeLast(5).ToList();
        if (historyTurns.Count == 0)
        {
            historyBuilder.AppendLine("(No prior turns in this session)");
        }
        else
        {
            foreach (var turn in historyTurns)
            {
                historyBuilder.AppendLine($"User: {turn.UserMessage}");
                historyBuilder.AppendLine($"Assistant: {turn.AssistantMessage}");
            }
        }

        return template
            .Replace("{{DOMAIN_NAME}}", domain.Name)
            .Replace("{{DOMAIN_MODE}}", domain.Mode.ToString())
            .Replace("{{RETRIEVED_CHUNKS}}", chunksBuilder.ToString().TrimEnd())
            .Replace("{{GROUNDING_POLICY}}", policy)
            .Replace("{{CONVERSATION_HISTORY}}", historyBuilder.ToString().TrimEnd())
            .Replace("{{USER_INPUT}}", userMessage);
    }
}
