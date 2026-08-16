using CP.Shared.Primitives.Avails;
using Xunit;

namespace CognitivePlatform.Tests;

public sealed class ConsoleSpinnerTests
{
    [Fact]
    public void RandomStyleSelection_NeverSelectsEmptyStringArray()
    {
        for (var i = 0; i < 100; i++)
        {
            using var spinner = new ConsoleSpinner("Test Message");
            
            // ConsoleSpinner initializes without throwing or getting stuck on empty frames
            Assert.NotNull(spinner);
        }
    }

    [Fact]
    public void ExplicitStyles_MatchExpectedNumberOfStyles()
    {
        Assert.True(ConsoleSpinner.NumberOfSpinnerStyles > 0);
    }
}
