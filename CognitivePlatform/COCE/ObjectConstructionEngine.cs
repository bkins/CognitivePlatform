using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CognitivePlatform.Api.COCE;

public class ObjectConstructionEngine : IObjectConstructionEngine
{
    private static readonly JsonSerializerOptions Options = new()
                                                            {
                                                                PropertyNameCaseInsensitive = true
                                                              , Converters = 
                                                                { 
                                                                    new JsonStringEnumConverter()
                                                                  , new LocalDateTimeOffsetConverter()
                                                                }
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

public class LocalDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        if (string.IsNullOrWhiteSpace(str))
            return default;

        if (DateTimeOffset.TryParse(str, out var dto))
        {
            if (dto.Offset == TimeSpan.Zero)
            {
                var localOffset = TimeZoneInfo.Local.GetUtcOffset(dto.DateTime);
                return new DateTimeOffset(dto.DateTime, localOffset);
            }
            return dto;
        }

        return default;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("O"));
    }
}
