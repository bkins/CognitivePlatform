using System.ComponentModel;
using CP.Shared.Primitives.Avails.Extensions;
using Xunit;

namespace CognitivePlatform.Tests;

public enum TestAuditEnum
{
    [Description("First Option Description")]
    FirstOption
  , SecondOption
}

public sealed class EnumExtensionsTests
{
    [Fact]
    public void GetDescription_WithDescriptionAttribute_ReturnsDescription()
    {
        var value = TestAuditEnum.FirstOption;

        var description = value.GetDescription();

        Assert.Equal("First Option Description", description);
    }

    [Fact]
    public void GetDescription_WithoutDescriptionAttribute_ReturnsToString()
    {
        var value = TestAuditEnum.SecondOption;

        var description = value.GetDescription();

        Assert.Equal("SecondOption", description);
    }

    [Fact]
    public void GetDescription_WithNullEnum_ReturnsEmptyString()
    {
        TestAuditEnum? value = null;

        var description = value.GetDescription();

        Assert.Equal(string.Empty, description);
    }

    [Fact]
    public void GetDescription_WithUndefinedEnumValue_ReturnsIntegerString()
    {
        var value = (TestAuditEnum)999;

        var description = value.GetDescription();

        Assert.Equal("999", description);
    }
}
