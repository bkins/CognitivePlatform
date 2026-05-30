using System.Net;
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
    private readonly IHealthProvider _health;

    public HealthActions(IHealthProvider health)
    {
        _health = health ?? throw new ArgumentNullException(nameof(health));
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
    public async Task<string> GetStepCount( [NaturalLanguageParam(Description = "Date or period to query, e.g. 'yesterday', 'today', 'last week'."
                                                                , AllowEmpty  = false)]
                                            string dateRange )
    {
        if (_health.IsConnected.Not()) return NotConnectedMessage();

        if (!TryResolveDateRange(dateRange, out var from, out var to))
            return $"I couldn't parse '{dateRange}' as a date or period. Try 'today', 'yesterday', or 'last week'.";

        try
        {
            var result = await _health.GetStepCountAsync(from, to);

            var sb = new StringBuilder();
            sb.Append($"Steps ({FormatDateRange(from, to)}): {result.Steps:N0}");

            if (result.DistanceMetres.HasValue)
                sb.Append($" · {result.DistanceMetres.Value / 1000:F1} km");

            if (result.ActiveCalories.HasValue)
                sb.Append($" · {result.ActiveCalories.Value:N0} kcal");

            return sb.ToString();
        }
        catch (HealthProviderException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return PermissionDeniedMessage();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            return HealthUnavailableMessage();
        }
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
    public async Task<string> GetSleepSummary( [NaturalLanguageParam(Description = "Date or period to query — use 'yesterday' for last night."
                                                                   , AllowEmpty  = false)]
                                               string dateRange )
    {
        if (_health.IsConnected.Not()) return NotConnectedMessage();

        if (!TryResolveDateRange(dateRange, out var from, out var to))
            return $"I couldn't parse '{dateRange}' as a date or period. Try 'yesterday', 'today', or 'last week'.";

        try
        {
            var result     = await _health.GetSleepAsync(from, to);
            var totalHours = result.TotalMinutes / 60;
            var totalMins  = result.TotalMinutes % 60;

            var sb = new StringBuilder();
            sb.Append($"Sleep ({FormatDateRange(from, to)}): {totalHours}h {totalMins}m total");

            if (result.DeepMinutes.HasValue)
                sb.Append($", {result.DeepMinutes.Value}m deep");

            if (result.RemMinutes.HasValue)
                sb.Append($", {result.RemMinutes.Value}m REM");

            if (result.LightMinutes.HasValue)
                sb.Append($", {result.LightMinutes.Value}m light");

            if (result.Sessions > 1)
                sb.Append($" ({result.Sessions} sessions)");

            return sb.ToString();
        }
        catch (HealthProviderException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return PermissionDeniedMessage();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            return HealthUnavailableMessage();
        }
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
    public async Task<string> GetHeartRate( [NaturalLanguageParam(Description = "Date or period to query, e.g. 'yesterday', 'today', 'last week'."
                                                                , AllowEmpty  = false)]
                                            string dateRange )
    {
        if (_health.IsConnected.Not()) return NotConnectedMessage();

        if (!TryResolveDateRange(dateRange, out var from, out var to))
            return $"I couldn't parse '{dateRange}' as a date or period. Try 'today', 'yesterday', or 'last week'.";

        try
        {
            var result = await _health.GetHeartRateAsync(from, to);

            var sb = new StringBuilder();
            sb.Append($"Heart rate ({FormatDateRange(from, to)}): avg {result.AverageBpm} bpm");

            if (result.MinBpm.HasValue && result.MaxBpm.HasValue)
                sb.Append($", {result.MinBpm.Value}–{result.MaxBpm.Value} bpm range");

            if (result.RestingBpm.HasValue)
                sb.Append($", resting {result.RestingBpm.Value} bpm");

            return sb.ToString();
        }
        catch (HealthProviderException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return PermissionDeniedMessage();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            return HealthUnavailableMessage();
        }
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
    public async Task<string> GetDistance( [NaturalLanguageParam(Description = "Date or period to query, e.g. 'yesterday', 'today', 'last week'."
                                                               , AllowEmpty  = false)]
                                           string dateRange )
    {
        if (_health.IsConnected.Not()) return NotConnectedMessage();

        if (!TryResolveDateRange(dateRange, out var from, out var to))
            return $"I couldn't parse '{dateRange}' as a date or period. Try 'today', 'yesterday', or 'last week'.";

        try
        {
            var result = await _health.GetDistanceAsync(from, to);
            var km     = result.Metres / 1000;

            var sb = new StringBuilder();
            sb.Append($"Distance ({FormatDateRange(from, to)}): {km:F2} km ({result.Metres:N0} m)");

            if (result.Steps.HasValue)
                sb.Append($" · {result.Steps.Value:N0} steps");

            if (result.ActiveCalories.HasValue)
                sb.Append($" · {result.ActiveCalories.Value:N0} kcal");

            return sb.ToString();
        }
        catch (HealthProviderException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return PermissionDeniedMessage();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            return HealthUnavailableMessage();
        }
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

    private static string NotConnectedMessage()
        => "Your health data is not connected yet. "
         + "Make sure the CP app is running on your phone and on the same local network, then try again.";

    private static string PermissionDeniedMessage()
        => "Health Connect permissions need to be granted on your phone. "
         + "Open Health Connect and allow the CP app to read your data.";

    private static string HealthUnavailableMessage()
        => "Your health data isn't available right now. "
         + "Make sure your phone is on the same network and the CP app is running, then try again.";
}
