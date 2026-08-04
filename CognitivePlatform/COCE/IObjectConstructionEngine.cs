using System;

namespace CognitivePlatform.Api.COCE;

public interface IObjectConstructionEngine
{
    object? Construct(string json, Type targetType);
}
