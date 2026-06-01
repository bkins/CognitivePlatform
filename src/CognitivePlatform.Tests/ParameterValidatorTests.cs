using CognitivePlatform.Api.Registry;

namespace CognitivePlatform.Tests;

public class ParameterValidatorTests
{
    private static ParameterDefinition MakeParam(string displayName, params string[] rules)
    {
        var param = new ParameterDefinition("fieldName", displayName, typeof(string))
                    {
                        ValidationRules = rules.ToList()
                    };
        return param;
    }

    // ================================================================
    // Required — invalid values
    // ================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Validate_Required_ReturnsError_WhenValueIsAbsent(string? value)
    {
        var param = MakeParam("Name", ParameterValidator.Required);

        var errors = ParameterValidator.Validate(param, value).ToList();

        Assert.Single(errors);
        Assert.Contains("required", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Required_ReturnsNoErrors_WhenValueIsPresent()
    {
        var param = MakeParam("Name", ParameterValidator.Required);

        var errors = ParameterValidator.Validate(param, "Alice").ToList();

        Assert.Empty(errors);
    }

    // ================================================================
    // MaxLength
    // ================================================================

    [Fact]
    public void Validate_MaxLength_ReturnsError_WhenValueExceedsLimit()
    {
        var param = MakeParam("Bio", "MaxLength:10");

        var errors = ParameterValidator.Validate(param, "12345678901").ToList();

        Assert.Single(errors);
        Assert.Contains("10", errors[0]);
    }

    [Theory]
    [InlineData("12345")]       // exactly at limit
    [InlineData("")]            // empty — under limit
    public void Validate_MaxLength_ReturnsNoErrors_WhenValueIsAtOrUnderLimit(string value)
    {
        var param = MakeParam("Bio", "MaxLength:5");

        var errors = ParameterValidator.Validate(param, value).ToList();

        Assert.Empty(errors);
    }

    // ================================================================
    // Integer
    // ================================================================

    [Theory]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("")]
    public void Validate_Integer_ReturnsError_WhenValueIsNotNumeric(string value)
    {
        var param = MakeParam("Age", ParameterValidator.Integer);

        var errors = ParameterValidator.Validate(param, value).ToList();

        Assert.Single(errors);
        Assert.Contains("integer", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Validate_Integer_ReturnsNoErrors_WhenValueIsInteger(string value)
    {
        var param = MakeParam("Age", ParameterValidator.Integer);

        var errors = ParameterValidator.Validate(param, value).ToList();

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Integer_ReturnsNoErrors_WhenValueIsNull()
    {
        var param = MakeParam("Age", ParameterValidator.Integer);

        var errors = ParameterValidator.Validate(param, null).ToList();

        Assert.Empty(errors);
    }

    // ================================================================
    // Date
    // ================================================================

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("32/13/2026")]
    public void Validate_Date_ReturnsError_WhenValueIsNotDate(string value)
    {
        var param = MakeParam("DueDate", ParameterValidator.Date);

        var errors = ParameterValidator.Validate(param, value).ToList();

        Assert.Single(errors);
        Assert.Contains("date", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("2025-04-19")]
    [InlineData("April 19, 2025")]
    public void Validate_Date_ReturnsNoErrors_WhenValueIsValidDate(string value)
    {
        var param = MakeParam("DueDate", ParameterValidator.Date);

        var errors = ParameterValidator.Validate(param, value).ToList();

        Assert.Empty(errors);
    }

    // ================================================================
    // Multi-rule / edge cases
    // ================================================================

    [Fact]
    public void Validate_MultipleRules_StopsAtFirstError_WhenValueIsNull()
    {
        var param = MakeParam("Code", ParameterValidator.Required, "MaxLength:3");

        var errors = ParameterValidator.Validate(param, null).ToList();

        Assert.Single(errors);
        Assert.Contains("required", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_NoRules_ReturnsNoErrors()
    {
        var param = MakeParam("Field");

        var errors = ParameterValidator.Validate(param, null).ToList();

        Assert.Empty(errors);
    }
}
