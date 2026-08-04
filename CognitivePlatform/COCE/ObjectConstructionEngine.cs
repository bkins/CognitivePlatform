using System;
using System.Text.Json;

namespace CognitivePlatform.Api.COCE;

public class ObjectConstructionEngine : IObjectConstructionEngine
{
    private static readonly JsonSerializerOptions Options = new()
                                                            {
                                                                PropertyNameCaseInsensitive = true
                                                              , Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                                                            };

    public object? Construct(string json, Type targetType)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize(json, targetType, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"COCE failed to construct object of type '{targetType.Name}' from JSON: {ex.Message}", ex);
        }
    }
}
