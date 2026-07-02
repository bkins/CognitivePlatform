using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Knowledge;
using CognitivePlatform.Api.Domains.Knowledge.Models;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Integrations.Embeddings;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public class KnowledgeActionsTests
{
    private readonly Mock<IKnowledgeIngestionService> _ingestionMock = new();
    private readonly Mock<ILlmRouter> _llmRouterMock = new();
    private readonly ConversationContext _context;
    private readonly KnowledgeActions _actions;

    public KnowledgeActionsTests()
    {
        _context = new ConversationContext("test-session");
        _actions = new KnowledgeActions(_ingestionMock.Object, _llmRouterMock.Object);
        _actions.SetSessionContext(_context);
    }

    [Fact]
    public async Task UseKnowledgeDomain_SetsActiveDomainInMetadata()
    {
        var domain = new KnowledgeDomain
        {
            Name = "HR",
            Mode = KnowledgeDomainMode.Grounded
        };
        _ingestionMock.Setup(i => i.GetDomainAsync("HR")).ReturnsAsync(domain);

        var result = await _actions.UseKnowledgeDomain("HR");

        Assert.Contains("Now using knowledge domain 'HR'", result);
        Assert.True(_context.Metadata.TryGetValue("active_knowledge_domain", out var activeDomain));
        Assert.Equal("HR", activeDomain);
    }

    [Fact]
    public async Task UseKnowledgeDomain_FailsIfDomainDoesNotExist()
    {
        _ingestionMock.Setup(i => i.GetDomainAsync("HR")).ReturnsAsync((KnowledgeDomain?)null);

        var result = await _actions.UseKnowledgeDomain("HR");

        Assert.Contains("does not exist", result);
        Assert.False(_context.Metadata.ContainsKey("active_knowledge_domain"));
    }

    [Fact]
    public void ClearKnowledgeDomain_RemovesDomainFromMetadata()
    {
        _context.Metadata["active_knowledge_domain"] = "HR";

        var result = _actions.ClearKnowledgeDomain();

        Assert.Contains("Cleared active knowledge domain", result);
        Assert.False(_context.Metadata.ContainsKey("active_knowledge_domain"));
    }

    [Fact]
    public async Task ListKnowledgeDomains_ReturnsRegisteredDomains()
    {
        var domains = new List<KnowledgeDomain>
        {
            new() { Name = "HR", Description = "HR policies", Mode = KnowledgeDomainMode.Strict },
            new() { Name = "Legal", Description = "Legal policies", Mode = KnowledgeDomainMode.Advisory }
        };
        _ingestionMock.Setup(i => i.ListDomainsAsync()).ReturnsAsync(domains);

        var result = await _actions.ListKnowledgeDomains();

        Assert.Contains("HR (Strict mode): HR policies", result);
        Assert.Contains("Legal (Advisory mode): Legal policies", result);
    }

    [Fact]
    public async Task QueryKnowledge_ReturnsUnknown_InStrictMode_WhenNoContextFound()
    {
        _context.Metadata["active_knowledge_domain"] = "HR";
        var domain = new KnowledgeDomain { Name = "HR", Mode = KnowledgeDomainMode.Strict };
        _ingestionMock.Setup(i => i.GetDomainAsync("HR")).ReturnsAsync(domain);
        _ingestionMock.Setup(i => i.RetrieveContextAsync("HR", "test query", 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());

        var result = await _actions.QueryKnowledge("test query");

        Assert.Equal("UNKNOWN", result);
    }
}
