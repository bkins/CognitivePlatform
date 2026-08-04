using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Integrations.Health;
using CognitivePlatform.Api.Registry.Domains;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Health;

[Domain(typeof(HealthDomain))]
public class HealthActions
{
    private readonly HealthDataCache _cache;

    public HealthActions(HealthDataCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    // -----------------------------------------------------------------------
    // GetStepCount
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Shows the step count for a date or period from your connected health device."
                         , Examples = new[]
                                      {
                                              "How many steps did I walk yesterday?"
                                            , "steps today"
                                            , "How many steps did I take this week?"
                                            , "step count last week"
                                      }
                         , Category = "health")]
    public Task<string> GetStepCount( [NaturalLanguageParam(Description = "Date or period to query, e.g. 'yesterday', 'today', 'last week'."
                                                           , AllowEmpty  = false)]
                                       string dateRange )
    {
        if (!TryResolveDateRange(dateRange, out var from, out var to))
            return Task.FromResult($"I couldn't parse '{dateRange}' as a date or period. Try 'today', 'yesterday', or 'last week'.");

        var date = DateOnly.FromDateTime(from.LocalDateTime.Date);

        if (!_cache.TryGet(date, out var snapshot))
            return Task.FromResult(NoDataMessage(date));

        var sb = new StringBuilder();
        sb.Append($"Steps ({FormatDateRange(from, to)}): {snapshot!.Steps:N0}");

        if (snapshot.DistanceMetres > 0)
            sb.Append($" · {snapshot.DistanceMetres / 1000:F1} km");

        return Task.FromResult(sb.ToString());
    }

    // -----------------------------------------------------------------------
    // GetSleepSummary
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Shows sleep data for a date or period from your connected health device."
                         , Examples = new[]
                                      {
                                              "How was my sleep last night?"
                                            , "sleep summary"
                                            , "How much did I sleep this week?"
                                            , "sleep last week"
                                      }
                         , Category = "health")]
    public Task<string> GetSleepSummary( [NaturalLanguageParam(Description = "Date or period to query — use 'yesterday' for last night."
                                                              , AllowEmpty  = false)]
                                          string dateRange )
    {
        if (!TryResolveDateRange(dateRange, out var from, out var to))
            return Task.FromResult($"I couldn't parse '{dateRange}' as a date or period. Try 'yesterday', 'today', or 'last week'.");

        var date = DateOnly.FromDateTime(from.LocalDateTime.Date);

        if (!_cache.TryGet(date, out var snapshot))
            return Task.FromResult(NoDataMessage(date));

        var totalHours = snapshot!.SleepMinutes / 60;
        var totalMins  = snapshot.SleepMinutes % 60;

        var sb = new StringBuilder();
        sb.Append($"Sleep ({FormatDateRange(from, to)}): {totalHours}h {totalMins}m total");

        if (snapshot.SleepSessions > 1)
            sb.Append($" ({snapshot.SleepSessions} sessions)");

        return Task.FromResult(sb.ToString());
    }

    // -----------------------------------------------------------------------
    // GetHeartRate
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Shows heart rate data for a date or period from your connected health device."
                         , Examples = new[]
                                      {
                                              "What was my heart rate yesterday?"
                                            , "resting heart rate"
                                            , "heart rate today"
                                            , "average bpm last week"
                                      }
                         , Category = "health")]
    public Task<string> GetHeartRate( [NaturalLanguageParam(Description = "Date or period to query, e.g. 'yesterday', 'today', 'last week'."
                                                           , AllowEmpty  = false)]
                                       string dateRange )
    {
        if (!TryResolveDateRange(dateRange, out var from, out var to))
            return Task.FromResult($"I couldn't parse '{dateRange}' as a date or period. Try 'today', 'yesterday', or 'last week'.");

        var date = DateOnly.FromDateTime(from.LocalDateTime.Date);

        if (!_cache.TryGet(date, out var snapshot))
            return Task.FromResult(NoDataMessage(date));

        var sb = new StringBuilder();
        sb.Append($"Heart rate ({FormatDateRange(from, to)}): avg {snapshot!.AverageHeartRate} bpm");

        if (snapshot.MinHeartRate > 0 && snapshot.MaxHeartRate > 0)
            sb.Append($", {snapshot.MinHeartRate}–{snapshot.MaxHeartRate} bpm range");

        return Task.FromResult(sb.ToString());
    }

    // -----------------------------------------------------------------------
    // GetDistance
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Shows distance covered for a date or period from your connected health device."
                         , Examples = new[]
                                      {
                                              "How far did I walk this week?"
                                            , "distance today"
                                            , "How far did I run yesterday?"
                                            , "distance last week"
                                      }
                         , Category = "health")]
    public Task<string> GetDistance( [NaturalLanguageParam(Description = "Date or period to query, e.g. 'yesterday', 'today', 'last week'."
                                                          , AllowEmpty  = false)]
                                      string dateRange )
    {
        if (!TryResolveDateRange(dateRange, out var from, out var to))
            return Task.FromResult($"I couldn't parse '{dateRange}' as a date or period. Try 'today', 'yesterday', or 'last week'.");

        var date = DateOnly.FromDateTime(from.LocalDateTime.Date);

        if (!_cache.TryGet(date, out var snapshot))
            return Task.FromResult(NoDataMessage(date));

        var km = snapshot!.DistanceMetres / 1000;

        var sb = new StringBuilder();
        sb.Append($"Distance ({FormatDateRange(from, to)}): {km:F2} km ({snapshot.DistanceMetres:N0} m)");

        if (snapshot.Steps > 0)
            sb.Append($" · {snapshot.Steps:N0} steps");

        return Task.FromResult(sb.ToString());
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static bool TryResolveDateRange( string            dateRange
                                           , out DateTimeOffset from
                                           , out DateTimeOffset to )
    {
        from = default;
        to   = default;

        var normalized  = dateRange.Trim().ToLowerInvariant();
        var today       = DateTimeOffset.Now.Date;
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(today);

        if (normalized is "last week" or "this week" or "past week" or "past 7 days")
        {
            from = new DateTimeOffset(today.AddDays(-7), localOffset);
            to   = new DateTimeOffset(today.AddDays(1),  localOffset);
            return true;
        }

        if (!TaskDateParser.TryParseDate(dateRange, out var parsed)) return false;

        var day    = parsed.LocalDateTime.Date;
        var offset = TimeZoneInfo.Local.GetUtcOffset(day);
        from = new DateTimeOffset(day,            offset);
        to   = new DateTimeOffset(day.AddDays(1), offset);
        return true;
    }

    private static string FormatDateRange(DateTimeOffset from, DateTimeOffset to)
    {
        var days = (to - from).TotalDays;
        return days <= 1
                   ? from.LocalDateTime.Date.ToString("yyyy-MM-dd")
                   : $"{from.LocalDateTime.Date:yyyy-MM-dd} – {to.LocalDateTime.Date.AddDays(-1):yyyy-MM-dd}";
    }

    private static string NoDataMessage(DateOnly date)
        => $"No health data has been received for {date:yyyy-MM-dd} yet. "
         + "Open the CP app on your phone and let it sync, then try again."
         + " (Data refreshes automatically every 5 minutes when the app is running.)";

    private static string PermissionDeniedMessage()
        => "Health Connect permissions need to be granted on your phone. "
         + "Open Health Connect and allow the CP app to read your data.";
}
