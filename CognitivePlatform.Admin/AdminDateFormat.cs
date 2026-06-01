namespace CognitivePlatform.Admin;

internal static class AdminDateFormat
{
    private static readonly TimeZoneInfo PacificTime =
        TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

    internal static string Short(DateTimeOffset dt)
    {
        var pt  = TimeZoneInfo.ConvertTime(dt, PacificTime);
        var utc = dt.ToUniversalTime();
        return $"{pt:yyyy-MM-dd HH:mm} PT ({utc:HH:mm} UTC)";
    }

    internal static string Long(DateTimeOffset dt)
    {
        var pt  = TimeZoneInfo.ConvertTime(dt, PacificTime);
        var utc = dt.ToUniversalTime();
        return $"{pt:yyyy-MM-dd HH:mm:ss} PT ({utc:HH:mm:ss} UTC)";
    }

    internal static string Short(DateTimeOffset? dt)
        => dt.HasValue ? Short(dt.Value) : "—";

    internal static string Long(DateTimeOffset? dt)
        => dt.HasValue ? Long(dt.Value) : "—";
}
