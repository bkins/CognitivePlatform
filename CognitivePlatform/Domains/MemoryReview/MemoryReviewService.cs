using CognitivePlatform.Api.Domains.Conversations;
using CognitivePlatform.Api.Domains.Identity;
using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;
using CognitivePlatform.Api.KnowledgeInbox;
using CognitivePlatform.Api.KnowledgeInbox.Interfaces;

namespace CognitivePlatform.Api.Domains.MemoryReview;

/// <summary>
/// Read-only, source-owned projection for the Memory review experience.
/// </summary>
public sealed class MemoryReviewService : IMemoryReviewService
{
    private readonly IIdentityService     _identityService;
    private readonly IPersonaService      _personaService;
    private readonly IPersonaStore        _personaStore;
    private readonly IConversationService _conversationService;
    private readonly IKnowledgeService    _knowledgeService;

    public MemoryReviewService( IIdentityService     identityService
                              , IPersonaService      personaService
                              , IPersonaStore        personaStore
                              , IConversationService conversationService
                              , IKnowledgeService    knowledgeService )
    {
        _identityService     = identityService;
        _personaService      = personaService;
        _personaStore        = personaStore;
        _conversationService = conversationService;
        _knowledgeService    = knowledgeService;
    }

    public async Task<MemoryReviewResult> GetReviewAsync(CancellationToken cancellationToken = default)
    {
        var items   = new List<MemoryReviewItem>();
        var sources = new List<MemoryReviewSourceStatus>();

        await AddIdentityItemsAsync(items, sources, cancellationToken);
        await AddPersonaItemsAsync(items, sources, cancellationToken);
        await AddConversationItemsAsync(items, sources, cancellationToken);
        AddKnowledgeItems(items, sources, cancellationToken);

        return new MemoryReviewResult(
            items.OrderBy(item => item.State)
                 .ThenByDescending(item => item.LastReinforcedOrModifiedUtc)
                 .ThenBy(item => item.SourceKind)
                 .ToList(),
            sources);
    }

    private async Task AddIdentityItemsAsync( List<MemoryReviewItem>         items
                                            , List<MemoryReviewSourceStatus> sources
                                            , CancellationToken               cancellationToken )
    {
        try
        {
            var assertions = await _identityService.GetAssertionsAsync(cancellationToken);
            items.AddRange(assertions.Select(assertion => new MemoryReviewItem(
                MemoryReviewSourceKind.Identity,
                assertion.Id,
                "Identity assertion",
                assertion.UserConfirmed ? MemoryReviewState.Confirmed : MemoryReviewState.NeedsReview,
                assertion.Statement,
                assertion.Confidence,
                new DateTimeOffset(assertion.LastReinforced),
                [ $"Topic: {assertion.Topic}", $"First observed: {assertion.FirstObserved:O}" ],
                [])));
            sources.Add(new MemoryReviewSourceStatus(MemoryReviewSourceKind.Identity, true));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            sources.Add(new MemoryReviewSourceStatus(MemoryReviewSourceKind.Identity, false, "Identity memories are temporarily unavailable."));
        }
    }

    private async Task AddPersonaItemsAsync( List<MemoryReviewItem>         items
                                           , List<MemoryReviewSourceStatus> sources
                                           , CancellationToken               cancellationToken )
    {
        try
        {
            var personas = await _personaService.ListAsync(cancellationToken);
            foreach (var persona in personas)
            {
                var memories = await _personaStore.GetMemoriesAsync(persona.Id, cancellationToken);
                items.AddRange(memories.Select(memory => new MemoryReviewItem(
                    MemoryReviewSourceKind.Persona,
                    memory.Id.ToString(),
                    $"Persona: {persona.Name}",
                    ToReviewState(memory.State),
                    memory.Content,
                    memory.Confidence,
                    new DateTimeOffset(memory.LastModifiedUtc),
                    [ $"Source: {memory.Source}", $"Persona: {persona.Name}" ],
                    [])));
            }
            sources.Add(new MemoryReviewSourceStatus(MemoryReviewSourceKind.Persona, true));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            sources.Add(new MemoryReviewSourceStatus(MemoryReviewSourceKind.Persona, false, "Persona memories are temporarily unavailable."));
        }
    }

    private async Task AddConversationItemsAsync( List<MemoryReviewItem>         items
                                                , List<MemoryReviewSourceStatus> sources
                                                , CancellationToken               cancellationToken )
    {
        try
        {
            var conversations = await _conversationService.ListRecordingsAsync(cancellationToken);
            foreach (var conversation in conversations.Where(conversation => !conversation.IsDeleted))
            {
                var candidates = await _conversationService.GetMemoriesAsync(conversation.Id, cancellationToken);
                items.AddRange(candidates.Where(candidate => !candidate.IsDeleted)
                                         .Select(candidate => new MemoryReviewItem(
                                             MemoryReviewSourceKind.Conversation,
                                             candidate.Id.ToString(),
                                             $"Conversation: {conversation.Title}",
                                             ToReviewState(candidate.State),
                                             candidate.Content,
                                             candidate.Confidence,
                                             new DateTimeOffset(candidate.ExtractedAtUtc),
                                             [ $"Conversation: {conversation.Title}", $"Category: {candidate.Category}" ],
                                             [])));
            }
            sources.Add(new MemoryReviewSourceStatus(MemoryReviewSourceKind.Conversation, true));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            sources.Add(new MemoryReviewSourceStatus(MemoryReviewSourceKind.Conversation, false, "Conversation memories are temporarily unavailable."));
        }
    }

    private void AddKnowledgeItems( List<MemoryReviewItem>         items
                                  , List<MemoryReviewSourceStatus> sources
                                  , CancellationToken               cancellationToken )
    {
        try
        {
            var knowledgeItems = _knowledgeService.GetKnowledge(new KnowledgeQuery(), cancellationToken);
            items.AddRange(knowledgeItems.Where(item => item.Status != KnowledgeStatus.Deleted)
                                         .Select(item => new MemoryReviewItem(
                                             MemoryReviewSourceKind.Knowledge,
                                             item.Id.ToString(),
                                             $"Knowledge: {item.Kind}",
                                             item.Status == KnowledgeStatus.Archived ? MemoryReviewState.Archived : MemoryReviewState.Confirmed,
                                             item.Summary ?? item.Title,
                                             null,
                                             item.LastModifiedAt,
                                             [ $"Kind: {item.Kind}", $"Title: {item.Title}" ],
                                             [])));
            sources.Add(new MemoryReviewSourceStatus(MemoryReviewSourceKind.Knowledge, true));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            sources.Add(new MemoryReviewSourceStatus(MemoryReviewSourceKind.Knowledge, false, "Knowledge memories are temporarily unavailable."));
        }
    }

    private static MemoryReviewState ToReviewState(MemoryState state)
    {
        return state == MemoryState.Provisional
            ? MemoryReviewState.NeedsReview
            : MemoryReviewState.Confirmed;
    }
}
