using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Cognition;
using CognitivePlatform.Api.Insights;

namespace CognitivePlatform.Tests;

public class CognitionActionsTests
{
    private readonly Mock<IObjectStore>  _storeMock = new();
    private readonly CognitionActions    _actions;

    public CognitionActionsTests()
    {
        _actions = new CognitionActions(_storeMock.Object);
    }

    // ====================================================================
    // EnableCognitiveDistortionDetector
    // ====================================================================

    [Fact]
    public async Task EnableCognitiveDistortionDetector_SavesFlagWithIsEnabledTrue()
    {
        CognitiveDistortionSettings? savedSettings = null;

        _storeMock
            .Setup(store => store.Save(
                It.IsAny<CognitiveDistortionSettings>()
              , It.IsAny<string?>()
              , It.IsAny<string?>()))
            .Callback((CognitiveDistortionSettings settings, string? _, string? __) =>
                savedSettings = settings)
            .ReturnsAsync(CognitiveDistortionInsightProvider.FlagKey);

        await _actions.EnableCognitiveDistortionDetector();

        Assert.NotNull(savedSettings);
        Assert.True(savedSettings.IsEnabled);
    }

    [Fact]
    public async Task EnableCognitiveDistortionDetector_ReturnsConfirmationMessage()
    {
        _storeMock
            .Setup(store => store.Save(
                It.IsAny<CognitiveDistortionSettings>()
              , It.IsAny<string?>()
              , It.IsAny<string?>()))
            .ReturnsAsync(CognitiveDistortionInsightProvider.FlagKey);

        var result = await _actions.EnableCognitiveDistortionDetector();

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.Contains("on", result, StringComparison.OrdinalIgnoreCase);
    }

    // ====================================================================
    // DisableCognitiveDistortionDetector
    // ====================================================================

    [Fact]
    public async Task DisableCognitiveDistortionDetector_SavesFlagWithIsEnabledFalse()
    {
        CognitiveDistortionSettings? savedSettings = null;

        _storeMock
            .Setup(store => store.Save(
                It.IsAny<CognitiveDistortionSettings>()
              , It.IsAny<string?>()
              , It.IsAny<string?>()))
            .Callback((CognitiveDistortionSettings settings, string? _, string? __) =>
                savedSettings = settings)
            .ReturnsAsync(CognitiveDistortionInsightProvider.FlagKey);

        await _actions.DisableCognitiveDistortionDetector();

        Assert.NotNull(savedSettings);
        Assert.False(savedSettings.IsEnabled);
    }

    [Fact]
    public async Task DisableCognitiveDistortionDetector_ReturnsConfirmationMessage()
    {
        _storeMock
            .Setup(store => store.Save(
                It.IsAny<CognitiveDistortionSettings>()
              , It.IsAny<string?>()
              , It.IsAny<string?>()))
            .ReturnsAsync(CognitiveDistortionInsightProvider.FlagKey);

        var result = await _actions.DisableCognitiveDistortionDetector();

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.Contains("off", result, StringComparison.OrdinalIgnoreCase);
    }
}
