using CognitivePlatform.Api.Interpreter;

namespace CognitivePlatform.Tests;

public class ExecutionProfileSelectorTests
{
    private readonly ExecutionProfileSelector _selector = new();

    [Theory]
    [InlineData("Please research and compare these approaches.", ExecutionProfileKind.Research, TaskComplexity.Heavy)]
    [InlineData("Help me debug this compile exception.", ExecutionProfileKind.Coding, TaskComplexity.Heavy)]
    [InlineData("Delete the production configuration.", ExecutionProfileKind.SafetyCritical, TaskComplexity.Standard)]
    [InlineData("How is your day going?", ExecutionProfileKind.Chat, TaskComplexity.Light)]
    public void SelectProfile_KnownIntent_ReturnsDeterministicProfile(string input, ExecutionProfileKind expectedKind, TaskComplexity expectedComplexity)
    {
        var profile = _selector.SelectProfile(input);

        Assert.Equal(expectedKind, profile.Kind);
        Assert.Equal(expectedComplexity, profile.PreferredComplexity);
    }

    [Fact]
    public void SelectProfile_SafetyIntent_RequiresStrictConfirmation()
    {
        var profile = _selector.SelectProfile("Rotate the production secret.");

        Assert.True(profile.EnforceStrictConfirmation);
    }
}
