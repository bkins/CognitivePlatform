using Moq;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;
using CognitivePlatform.Api.Interpreter;

namespace CognitivePlatform.Tests;

public class MemoryReconstructionEngineTests
{
    private readonly Mock<ILlmRouter>    _routerMock = new();
    private readonly Mock<IPersonaStore> _storeMock  = new();
    private readonly MemoryReconstructionEngine _engine;

    public MemoryReconstructionEngineTests()
    {
        _engine = new MemoryReconstructionEngine(_routerMock.Object, _storeMock.Object);
    }

    // ================================================================
    // ExtractMemoryFragmentsAsync
    // ================================================================

    [Fact]
    public async Task ExtractMemoryFragments_ReturnsEmpty_WhenConversationTurnIsEmpty()
    {
        var result = await _engine.ExtractMemoryFragmentsAsync(Guid.NewGuid(), string.Empty);

        Assert.Empty(result);
        _routerMock.Verify(router => router.SendAsync(It.IsAny<string>()
                                                     , It.IsAny<ConversationContext>()
                                                     , It.IsAny<TaskComplexity>()
                                                     , It.IsAny<CancellationToken>())
                         , Times.Never);
    }

    [Fact]
    public async Task ExtractMemoryFragments_ReturnsEmpty_WhenLlmReturnsEmptyJsonArray()
    {
        _routerMock.Setup(router => router.SendAsync(It.IsAny<string>()
                                                    , It.IsAny<ConversationContext>()
                                                    , It.IsAny<TaskComplexity>()
                                                    , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new LlmResponse { Content = "[]" });

        var result = await _engine.ExtractMemoryFragmentsAsync(Guid.NewGuid(), "She smiled often.");

        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractMemoryFragments_ReturnsFragments_WhenLlmReturnsValidJson()
    {
        var personaId = Guid.NewGuid();
        var json      = """[{"content":"She loved hiking in the mountains.","type":"Historical"}]""";

        _routerMock.Setup(router => router.SendAsync(It.IsAny<string>()
                                                    , It.IsAny<ConversationContext>()
                                                    , It.IsAny<TaskComplexity>()
                                                    , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new LlmResponse { Content = json });

        var result = await _engine.ExtractMemoryFragmentsAsync(personaId, "She loved hiking in the mountains.");

        Assert.Single(result);
        Assert.Equal("She loved hiking in the mountains.", result[0].Content);
        Assert.Equal(MemoryType.Historical,                result[0].Type);
        Assert.Equal(MemoryState.Provisional,              result[0].State);
        Assert.Equal(MemorySource.ConversationRecollection, result[0].Source);
        Assert.Equal(personaId,                            result[0].PersonaId);
    }

    [Fact]
    public async Task ExtractMemoryFragments_ReturnsEmpty_WhenLlmThrows()
    {
        _routerMock.Setup(router => router.SendAsync(It.IsAny<string>()
                                                    , It.IsAny<ConversationContext>()
                                                    , It.IsAny<TaskComplexity>()
                                                    , It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _engine.ExtractMemoryFragmentsAsync(Guid.NewGuid(), "She used to sing.");

        Assert.Empty(result);
    }

    // ================================================================
    // AssignConfidenceAsync
    // ================================================================

    [Fact]
    public async Task AssignConfidence_UpdatesConfidence_WhenLlmReturnsValidScore()
    {
        var memory = new PersonaMemory { Id = Guid.NewGuid(), Content = "She was a nurse.", Confidence = 0.5f };

        _routerMock.Setup(router => router.SendAsync(It.IsAny<string>()
                                                    , It.IsAny<ConversationContext>()
                                                    , It.IsAny<TaskComplexity>()
                                                    , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new LlmResponse { Content = "0.82" });

        var result = await _engine.AssignConfidenceAsync(memory);

        Assert.Equal(0.82f, result.Confidence, precision: 4);
    }

    [Fact]
    public async Task AssignConfidence_RetainsExistingConfidence_WhenLlmReturnsInvalidScore()
    {
        var memory = new PersonaMemory { Id = Guid.NewGuid(), Content = "She was quiet.", Confidence = 0.6f };

        _routerMock.Setup(router => router.SendAsync(It.IsAny<string>()
                                                    , It.IsAny<ConversationContext>()
                                                    , It.IsAny<TaskComplexity>()
                                                    , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new LlmResponse { Content = "not a number" });

        var result = await _engine.AssignConfidenceAsync(memory);

        Assert.Equal(0.6f, result.Confidence, precision: 4);
    }

    // ================================================================
    // DetectContradictionsAsync
    // ================================================================

    [Fact]
    public async Task DetectContradictions_ReturnsEmpty_WhenNoCanonicalMemoriesExist()
    {
        _storeMock.Setup(store => store.GetMemoriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<PersonaMemory>());

        var candidate = new PersonaMemory { Id = Guid.NewGuid(), Content = "She had red hair." };

        var result = await _engine.DetectContradictionsAsync(Guid.NewGuid(), candidate);

        Assert.Empty(result);
        _routerMock.Verify(router => router.SendAsync(It.IsAny<string>()
                                                     , It.IsAny<ConversationContext>()
                                                     , It.IsAny<TaskComplexity>()
                                                     , It.IsAny<CancellationToken>())
                         , Times.Never);
    }

    [Fact]
    public async Task DetectContradictions_ReturnsEmpty_WhenLlmReportsNoConflict()
    {
        var existing = new PersonaMemory
                       {
                               Id       = Guid.NewGuid()
                             , Content  = "She had brown hair."
                             , State    = MemoryState.Canonical
                       };

        _storeMock.Setup(store => store.GetMemoriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<PersonaMemory> { existing });

        _routerMock.Setup(router => router.SendAsync(It.IsAny<string>()
                                                    , It.IsAny<ConversationContext>()
                                                    , It.IsAny<TaskComplexity>()
                                                    , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new LlmResponse { Content = """{"contradicts":false,"score":0.1,"reason":""}""" });

        var candidate = new PersonaMemory { Id = Guid.NewGuid(), Content = "She had dark hair." };

        var result = await _engine.DetectContradictionsAsync(Guid.NewGuid(), candidate);

        Assert.Empty(result);
    }

    // ================================================================
    // GenerateExcavationPromptAsync
    // ================================================================

    [Fact]
    public async Task GenerateExcavationPrompt_ReturnsNull_WhenConversationTurnIsEmpty()
    {
        var persona = new CanonicalPersona { Id = Guid.NewGuid(), Name = "Sarah" };

        _storeMock.Setup(store => store.GetMemoriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<PersonaMemory>());

        var result = await _engine.GenerateExcavationPromptAsync(persona, string.Empty);

        Assert.Null(result);
    }

    [Fact]
    public async Task GenerateExcavationPrompt_ReturnsNull_WhenLlmReturnsNullSentinel()
    {
        var persona = new CanonicalPersona { Id = Guid.NewGuid(), Name = "Sarah" };

        _storeMock.Setup(store => store.GetMemoriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<PersonaMemory>());

        _routerMock.Setup(router => router.SendAsync(It.IsAny<string>()
                                                    , It.IsAny<ConversationContext>()
                                                    , It.IsAny<TaskComplexity>()
                                                    , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new LlmResponse { Content = "NULL" });

        var result = await _engine.GenerateExcavationPromptAsync(persona, "She was always kind.");

        Assert.Null(result);
    }

    [Fact]
    public async Task GenerateExcavationPrompt_ReturnsQuestion_WhenLlmReturnsNonNullContent()
    {
        var persona   = new CanonicalPersona { Id = Guid.NewGuid(), Name = "Emma" };
        var question  = "You remember her laugh clearly — do you also remember her voice?";

        _storeMock.Setup(store => store.GetMemoriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<PersonaMemory>());

        _routerMock.Setup(router => router.SendAsync(It.IsAny<string>()
                                                    , It.IsAny<ConversationContext>()
                                                    , It.IsAny<TaskComplexity>()
                                                    , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new LlmResponse { Content = question });

        var result = await _engine.GenerateExcavationPromptAsync(persona, "She always laughed at my jokes.");

        Assert.Equal(question, result);
    }
}
