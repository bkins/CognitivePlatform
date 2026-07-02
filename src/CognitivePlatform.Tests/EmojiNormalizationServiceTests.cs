using CognitivePlatform.Api.Domains.Journal;
using Xunit;

namespace CognitivePlatform.Tests;

public sealed class EmojiNormalizationServiceTests
{
    [Theory]
    [InlineData(1, "😢")]
    [InlineData(2, "🙁")]
    [InlineData(3, "😐")]
    [InlineData(4, "🙂")]
    [InlineData(5, "😄")]
    [InlineData(null, "❓")]
    [InlineData(99, "😄")]
    public void MapValenceEmoji_ReturnsExpectedEmoji_ForMoodScore(int? moodScore, string expected)
    {
        Assert.Equal(expected, EmojiNormalizationService.MapValenceEmoji(moodScore));
    }

    [Theory]
    [InlineData("angry", "😡")]
    [InlineData("Very Mad", "😡")]
    [InlineData("livid", "🤬")]
    [InlineData("furious today", "🤬")]
    [InlineData("frustrated", "😤")]
    [InlineData("worried", "😰")]
    [InlineData("stressed out", "😖")]
    [InlineData("unhappy", "😢")]
    [InlineData("depressed", "😞")]
    [InlineData("lonely", "😔")]
    [InlineData("disappointed", "😕")]
    [InlineData("meh", "😐")]
    [InlineData("numb", "😐")]
    [InlineData("confused", "😕")]
    [InlineData("content", "🙂")]
    [InlineData("relaxed", "😌")]
    [InlineData("relieved", "😮‍💨")]
    [InlineData("happy", "😄")]
    [InlineData("excited", "🤩")]
    [InlineData("proud", "😊")]
    [InlineData("", "❓")]
    [InlineData("unknown_emotion", "❓")]
    public void MapAffectEmoji_ReturnsExpectedEmoji_ForMoodString(string mood, string expected)
    {
        Assert.Equal(expected, EmojiNormalizationService.MapAffectEmoji(mood));
    }
}
