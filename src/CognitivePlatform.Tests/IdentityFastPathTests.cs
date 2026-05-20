using Moq;
using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;

namespace CognitivePlatform.Tests;

public class IdentityFastPathTests
{
    private readonly Mock<IActionRegistry>           _registryMock    = new();
    private readonly Mock<IDailyRecordCommandParser> _dailyParserMock = new();
    private readonly FastPathResolver                _resolver;

    private static ActionMetadata MakeAction(string name)
        => new() { Name = name, Parameters = new List<ParameterMetadata>() };

    public IdentityFastPathTests()
    {
        var actions = new List<ActionMetadata>
        {
              MakeAction("GetProfile")
            , MakeAction("SetProfileField")
            , MakeAction("AddToProfileList")
            , MakeAction("RemoveFromProfileList")
            , MakeAction("AddIdentityAssertion")
            , MakeAction("ListIdentityAssertions")
            , MakeAction("ConfirmIdentityAssertion")
        };

        _registryMock.Setup(registry => registry.Actions).Returns(actions);
        _registryMock.Setup(registry => registry.FastPathActions).Returns(new List<ActionMetadata>());

        _dailyParserMock.Setup(parser => parser.Parse(It.IsAny<string>()))
                        .Returns(new ParsedDailyCommand { CommandType = DailyCommandType.Unknown });

        _resolver = new FastPathResolver(_registryMock.Object, _dailyParserMock.Object);
    }

    // ================================================================
    // GetProfile signals
    // ================================================================

    [Theory]
    [InlineData("show my profile")]
    [InlineData("get my profile")]
    [InlineData("my profile")]
    [InlineData("show profile")]
    [InlineData("who am i")]
    public void TryResolve_ResolvesToGetProfile_ForProfileQuerySignals(string input)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetProfile", action!.Name);
        Assert.Empty(parameters!);
    }

    // ================================================================
    // ListIdentityAssertions signals
    // ================================================================

    [Theory]
    [InlineData("show my identity assertions")]
    [InlineData("list my assertions")]
    [InlineData("my assertions")]
    [InlineData("show identity facts")]
    public void TryResolve_ResolvesToListIdentityAssertions_ForAssertionSignals(string input)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("ListIdentityAssertions", action!.Name);
        Assert.Empty(parameters!);
    }

    // ================================================================
    // No false positives
    // ================================================================

    [Fact]
    public void TryResolve_ReturnsFalse_ForUnrelatedInput()
    {
        var resolved = _resolver.TryResolve("show my tasks for today", out _, out _);

        Assert.False(resolved);
    }
}
