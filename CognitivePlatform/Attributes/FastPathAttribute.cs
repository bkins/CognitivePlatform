using System;

namespace CognitivePlatform.Api.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class FastPathAttribute : Attribute
{
    public string? Hint { get; set; }
}