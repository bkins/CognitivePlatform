using CP.Shared.Primitives.Avails.Extensions;
using Xunit;

namespace CognitivePlatform.Tests;

public sealed class StringExtensionsTests
{
    [Fact]
    public void IsEqualTo_WithDifferentCasing_ReturnsTrue()
    {
        var str1 = "TestString";
        var str2 = "teststring";

        var result = str1.IsEqualTo(str2);

        Assert.True(result);
    }

    [Fact]
    public void IsNotEqualTo_WithDifferentCasing_ReturnsFalse()
    {
        var str1 = "TestString";
        var str2 = "teststring";

        var result = str1.IsNotEqualTo(str2);

        Assert.False(result);
    }

    [Fact]
    public void IsNotEqualTo_WithDifferentText_ReturnsTrue()
    {
        var str1 = "TestString";
        var str2 = "DifferentString";

        var result = str1.IsNotEqualTo(str2);

        Assert.True(result);
    }

    [Fact]
    public void IsEqualTo_WithSameText_ReturnsTrue()
    {
        var str1 = "ExactMatch";
        var str2 = "ExactMatch";

        var result = str1.IsEqualTo(str2);

        Assert.True(result);
    }

    [Fact]
    public void IsNotEqualTo_WithSameText_ReturnsFalse()
    {
        var str1 = "ExactMatch";
        var str2 = "ExactMatch";

        var result = str1.IsNotEqualTo(str2);

        Assert.False(result);
    }
}
