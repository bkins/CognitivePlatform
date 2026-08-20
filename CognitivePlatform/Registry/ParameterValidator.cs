namespace CognitivePlatform.Api.Registry;

public static class ParameterValidator
{
    public const string Required  = nameof(Required);
    public const string MaxLength = nameof(MaxLength);
    public const string Integer   = nameof(Integer);
    public const string Date      = nameof(Date);

    public static IEnumerable<string> Validate(ParameterDefinition parameterDefinition
                                             , string?             rawValue)
    {
        foreach (var rule in parameterDefinition.ValidationRules)
        {
            if (rule == Required)
            {
                if (rawValue.HasNoValue())
                    yield return $"{parameterDefinition.DisplayName} is required.";
            }

            if (rule.StartsWithIgnoreCase($"{MaxLength}:"))
            {
                if (rawValue.HasValue()
                 && rawValue.Length > int.Parse(rule.Split(':')[1]))
                {
                    yield return $"{parameterDefinition.DisplayName} must not exceed {rule.Split(':')[1]} characters.";
                }
            }

            if (rule == Integer)
            {
                if (rawValue is not null && !int.TryParse(rawValue, out _))
                    yield return $"{parameterDefinition.DisplayName} must be a valid integer.";
            }

            if (rule == Date)
            {
                if (rawValue is not null && !DateOnly.TryParse(rawValue, out _))
                    yield return $"{parameterDefinition.DisplayName} must be a valid date.";
            }
        }
    }
}
