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
    // Required
    // ================================================================

    [Fact]
    public void Validate_Required_ReturnsError_WhenValueIsNull()
    {
        var param = MakeParam("Name", ParameterValidator.Required);

        var errors = ParameterValidator.Validate(param, null).ToList();

        Assert.Single(errors);
        Assert.Contains("required", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Required_ReturnsError_WhenValueIsWhitespace()
    {
        var param = MakeParam("Name", ParameterValidator.Required);

        var errors = ParameterValidator.Validate(param, "   ").ToList();

        Assert.Single(errors);
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

    [Fact]
    public void Validate_MaxLength_ReturnsNoErrors_WhenValueIsExactlyLimit()
    {
        var param = MakeParam("Bio", "MaxLength:5");

        var errors = ParameterValidator.Validate(param, "12345").ToList();

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_MaxLength_ReturnsNoErrors_WhenValueIsEmpty()
    {
        var param = MakeParam("Bio", "MaxLength:5");

        var errors = ParameterValidator.Validate(param, string.Empty).ToList();

        Assert.Empty(errors);
    }

    // ================================================================
    // Integer
    // ================================================================

    [Fact]
    public void Validate_Integer_ReturnsError_WhenValueIsNotNumeric()
    {
        var param = MakeParam("Age", ParameterValidator.Integer);

        var errors = ParameterValidator.Validate(param, "abc").ToList();

        Assert.Single(errors);
        Assert.Contains("integer", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Integer_ReturnsNoErrors_WhenValueIsNumeric()
    {
        var param = MakeParam("Age", ParameterValidator.Integer);

        var errors = ParameterValidator.Validate(param, "42").ToList();

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

    [Fact]
    public void Validate_Date_ReturnsError_WhenValueIsNotDate()
    {
        var param = MakeParam("DueDate", ParameterValidator.Date);

        var errors = ParameterValidator.Validate(param, "not-a-date").ToList();

        Assert.Single(errors);
        Assert.Contains("date", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Date_ReturnsNoErrors_WhenValueIsValidDate()
    {
        var param = MakeParam("DueDate", ParameterValidator.Date);

        var errors = ParameterValidator.Validate(param, "2025-04-19").ToList();

        Assert.Empty(errors);
    }

    // ================================================================
    // Multi-rule
    // ================================================================

    [Fact]
    public void Validate_MultipleRules_ReturnsAllErrors_WhenValueViolatesAll()
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
