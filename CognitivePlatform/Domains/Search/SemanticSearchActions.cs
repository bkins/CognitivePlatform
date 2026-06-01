using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Integrations.Embeddings;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Domains.Search;

[Domain(typeof(SemanticSearchDomain))]
public class SemanticSearchActions
{
    private const int    SnippetLength = 150;
    private const int    DefaultTopK   = 5;

    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore      _vectorStore;

    public SemanticSearchActions(IEmbeddingService embeddingService, IVectorStore vectorStore)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _vectorStore      = vectorStore      ?? throw new ArgumentNullException(nameof(vectorStore));
    }

    // -----------------------------------------------------------------------
    // SemanticSearch — cross-domain query
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(
        Description = "Semantically searches all your journal entries, tasks, and daily records for content related to the query."
      , Examples    = new[]
        {
                "What have I written about burnout?"
              , "Find anything related to project planning"
              , "Search my notes for stress"
              , "What do I know about time management?"
              , "Find entries about work-life balance"
        }
      , Category = "search"
    )]
    public async Task<string> SemanticSearch(
        [NaturalLanguageParam(Description = "The topic or concept to search for across all your content.")]
        string query )
    {
        if (!_embeddingService.IsAvailable)
            return UnavailableMessage();

        try
        {
            var embedding = await _embeddingService.EmbedAsync(query);
            var results   = await _vectorStore.SearchAsync(embedding, topK: DefaultTopK);

            return results.Count == 0
                       ? $"No results found for '{query}'."
                       : FormatResults(results, query, domain: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            return EmbeddingErrorMessage();
        }
    }

    // -----------------------------------------------------------------------
    // FindSimilar — find items similar to an existing entry
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(
        Description = "Finds items within a domain that are semantically similar to a specific existing entry."
      , Examples    = new[]
        {
                "Find tasks similar to this"
              , "Show me related journal entries"
              , "What journal entries are similar to my burnout entry?"
        }
      , Category = "search"
    )]
    public async Task<string> FindSimilar(
        [NaturalLanguageParam(Description = "The domain to search within, e.g. 'journal', 'task', 'daily', 'knowledge'.")]
        string domain
      , [NaturalLanguageParam(Description = "The reference ID of the source item to find similar content for.")]
        string referenceId )
    {
        if (!_embeddingService.IsAvailable)
            return UnavailableMessage();

        if (string.IsNullOrWhiteSpace(domain))
            return "Please specify a domain (e.g. 'journal', 'task', 'daily').";

        if (string.IsNullOrWhiteSpace(referenceId))
            return "Please specify the ID of the item to find similar content for.";

        try
        {
            var source = await _vectorStore.GetByReferenceAsync(domain, referenceId);

            if (source is null)
                return $"No indexed content found for {domain} entry '{referenceId}'. "
                     + "The item may not have been embedded yet — try again in a moment.";

            var results = await _vectorStore.SearchAsync(source.Embedding, topK: DefaultTopK + 1, domain: domain);

            var filtered = results
                           .Where(result => result.Entry.ReferenceId != referenceId)
                           .Take(DefaultTopK)
                           .ToList();

            if (filtered.Count == 0)
                return $"No similar {domain} entries found for '{referenceId}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Items in {domain} similar to '{referenceId}':");
            sb.AppendLine();

            foreach (var result in filtered)
            {
                var snippet = Snippet(result.Entry.Text);
                sb.AppendLine($"[{result.Entry.Domain}] {result.Entry.ReferenceId} (score: {result.Score:F2})");
                sb.AppendLine($"  {snippet}");
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            return EmbeddingErrorMessage();
        }
    }

    // -----------------------------------------------------------------------
    // SemanticSearchInDomain — domain-scoped query
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(
        Description = "Semantically searches a specific domain (journal, tasks, daily, knowledge) for content related to the query."
      , Examples    = new[]
        {
                "Find journal entries about anxiety"
              , "Search my tasks for fitness"
              , "Look in my journal for gratitude"
              , "Find daily records about productivity"
        }
      , Category = "search"
    )]
    public async Task<string> SemanticSearchInDomain(
        [NaturalLanguageParam(Description = "The domain to search within, e.g. 'journal', 'task', 'daily', 'knowledge'.")]
        string domain
      , [NaturalLanguageParam(Description = "The topic or concept to search for within this domain.")]
        string query )
    {
        if (!_embeddingService.IsAvailable)
            return UnavailableMessage();

        if (string.IsNullOrWhiteSpace(domain))
            return "Please specify a domain (e.g. 'journal', 'task', 'daily').";

        try
        {
            var embedding = await _embeddingService.EmbedAsync(query);
            var results   = await _vectorStore.SearchAsync(embedding, topK: DefaultTopK, domain: domain);

            return results.Count == 0
                       ? $"No {domain} results found for '{query}'."
                       : FormatResults(results, query, domain);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            return EmbeddingErrorMessage();
        }
    }

    // -----------------------------------------------------------------------
    // Formatting helpers
    // -----------------------------------------------------------------------

    private static string FormatResults(
        IReadOnlyList<VectorSearchResult> results
      , string                           query
      , string?                          domain )
    {
        var sb      = new StringBuilder();
        var header  = domain is null
                          ? $"Results for '{query}':"
                          : $"Results in {domain} for '{query}':";

        sb.AppendLine(header);
        sb.AppendLine();

        foreach (var result in results)
        {
            var snippet = Snippet(result.Entry.Text);
            sb.AppendLine($"[{result.Entry.Domain}] {result.Entry.ReferenceId} (score: {result.Score:F2})");
            sb.AppendLine($"  {snippet}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string Snippet(string text)
        => text.Length <= SnippetLength ? text : text[..SnippetLength] + "…";

    private static string UnavailableMessage()
        => "Semantic search isn't available yet — make sure Ollama is running with the nomic-embed-text model.";

    private static string EmbeddingErrorMessage()
        => "Semantic search encountered an error. Make sure Ollama is running with the nomic-embed-text model.";
}
