
namespace CognitivePlatform.Api.Avails.Extensions;

public static class BoolExtensions
{
    [Obsolete("Use CP.Shared.Primitives.Avails.Extensions instead")]
    public static bool Not (this bool value)
    {
        return ! value;
    }
}