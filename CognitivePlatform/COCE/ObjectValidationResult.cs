namespace CognitivePlatform.Api.COCE;

public sealed record ObjectValidationResult
{
    public bool                  IsValid           { get; init; }
    public IReadOnlyList<string> Errors            { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingProperties { get; init; } = Array.Empty<string>();

    public static ObjectValidationResult Success() => new() { IsValid = true };

    public static ObjectValidationResult Failure(IEnumerable<string> errors, IEnumerable<string>? missingProperties = null)
    {
        return new ObjectValidationResult
               {
                       IsValid           = false
                     , Errors            = errors.ToList()
                     , MissingProperties = missingProperties?.ToList() ?? new List<string>()
               };
    }
}
