using System;

namespace CognitivePlatform.Api.COCE;

public interface IObjectConstructionEngine
{
    object? Construct(string json, Type targetType);
    bool TryConstruct(string json, Type targetType, out object? result, out ObjectValidationResult validation);
}
