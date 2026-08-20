namespace CognitivePlatform.Api.Data;

/// <summary>
/// Default system date provider with development environment override support (CP_DAILY_DATE).
/// </summary>
public class SystemDateProvider : IDateProvider
{
    public DateOnly Today
    {
        get
        {
            var envOverride = Environment.GetEnvironmentVariable("CP_DAILY_DATE");
            if (envOverride.HasValue()
             && DateOnly.TryParse(envOverride, out var overrideDate))
            {
                return overrideDate;
            }

            return DateOnly.FromDateTime(DateTime.Now);
        }
    }

    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime Now    => DateTime.Now;
}
