using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CognitivePlatform.Api.COCE;

public class ObjectConstructionEngine : IObjectConstructionEngine
{
    private readonly IEnumerable<IObjectValidator> _validators;

    private static readonly JsonSerializerOptions Options = new()
                                                            {
                                                                PropertyNameCaseInsensitive = true
                                                              , Converters = 
                                                                { 
                                                                    new JsonStringEnumConverter()
                                                                  , new LocalDateTimeOffsetConverter()
                                                                }
                                                            };

    public ObjectConstructionEngine(IEnumerable<IObjectValidator>? validators = null)
    {
        _validators = validators ?? Enumerable.Empty<IObjectValidator>();
    }

    public object? Construct(string json, Type targetType)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        object? deserialized;
        try
        {
            deserialized = JsonSerializer.Deserialize(json, targetType, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"COCE failed to construct object of type '{targetType.Name}' from JSON: {ex.Message}", ex);
        }

        if (deserialized is not null)
        {
            var validator = _validators.FirstOrDefault(v => v.CanValidate(targetType));
            if (validator is not null)
            {
                var validation = validator.Validate(deserialized);
                if (!validation.IsValid)
                {
                    var errors = string.Join("; ", validation.Errors);
                    throw new InvalidOperationException($"COCE validation failed for '{targetType.Name}': {errors}");
                }
            }
        }

        return deserialized;
    }

    public bool TryConstruct(string json, Type targetType, out object? result, out ObjectValidationResult validation)
    {
        result     = null;
        validation = ObjectValidationResult.Success();

        if (string.IsNullOrWhiteSpace(json))
        {
            validation = ObjectValidationResult.Failure(new[] { "Input JSON was empty or whitespace." });
            return false;
        }

        try
        {
            result = JsonSerializer.Deserialize(json, targetType, Options);
        }
        catch (JsonException ex)
        {
            validation = ObjectValidationResult.Failure(new[] { $"JSON deserialization error: {ex.Message}" });
            return false;
        }

        if (result is null)
        {
            validation = ObjectValidationResult.Failure(new[] { "Deserialized object was null." });
            return false;
        }

        var validator = _validators.FirstOrDefault(v => v.CanValidate(targetType));
        if (validator is not null)
        {
            validation = validator.Validate(result);
            return validation.IsValid;
        }

        return true;
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
