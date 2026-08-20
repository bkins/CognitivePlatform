using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Registry.Domains;
using CognitivePlatform.Api.Wellbeing;

namespace CognitivePlatform.Api.Domains.Wellbeing;

[Domain(typeof(WellbeingDomain))]
public class WellbeingActions
{
    private readonly IWellbeingPatternService _patternService;

    public WellbeingActions(IWellbeingPatternService patternService)
    {
        _patternService = patternService ?? throw new ArgumentNullException(nameof(patternService));
    }

    // -----------------------------------------------------------------------
    // GetWellbeingReport
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Provides a cross-domain wellbeing summary covering sleep, activity, tasks, and journal patterns."
                         , Examples = new[]
                                      {
                                              "How has my wellbeing been this week?"
                                            , "wellbeing report for last 7 days"
                                            , "How am I doing overall?"
                                            , "wellbeing summary"
                                      }
                         , Category = "wellbeing")]
    public async Task<string> GetWellbeingReport( [NaturalLanguageParam(Description = "Date range to analyse, e.g. 'last week', 'last 7 days'. Defaults to last 7 days."
                                                                      , AllowEmpty  = true)]
                                                  string dateRange )
    {
        var (from, to) = ResolveDefaultDateRange(dateRange);

        try
        {
            var report = await _patternService.AnalyseAsync(from, to);
            return report.NarrativeSummary;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            return "I wasn't able to generate a wellbeing report right now. Try again in a moment.";
        }
    }

    // -----------------------------------------------------------------------
    // GetWellbeingPatterns
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Lists all detected patterns across health, activity, tasks, and journal data for a period."
                         , Examples = new[]
                                      {
                                              "What patterns do you see?"
                                            , "any concerning trends?"
                                            , "show my wellbeing patterns"
                                            , "what trends have you noticed?"
                                      }
                         , Category = "wellbeing")]
    public async Task<string> GetWellbeingPatterns( [NaturalLanguageParam(Description = "Date range to analyse, e.g. 'last week', 'last 7 days'. Defaults to last 7 days."
                                                                        , AllowEmpty  = true)]
                                                    string dateRange )
    {
        var (from, to) = ResolveDefaultDateRange(dateRange);

        try
        {
            var report = await _patternService.AnalyseAsync(from, to);

            if (report.Patterns.Count == 0)
                return "No notable patterns detected for this period. Things look balanced.";

            var sb = new StringBuilder();
            sb.AppendLine($"Patterns detected ({from:yyyy-MM-dd} – {to:yyyy-MM-dd}):");
            sb.AppendLine();

            foreach (var pattern in report.Patterns)
                sb.AppendLine($"[{pattern.Severity}] {pattern.Description}");

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            return "I wasn't able to retrieve wellbeing patterns right now. Try again in a moment.";
        }
    }

    // -----------------------------------------------------------------------
    // CheckSleepHealth
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Checks whether you've been getting enough sleep by analysing recent sleep signals."
                         , Examples = new[]
                                      {
                                              "Am I sleeping enough?"
                                            , "sleep quality this week"
                                            , "How has my sleep been?"
                                            , "am I getting enough sleep?"
                                      }
                         , Category = "wellbeing")]
    public async Task<string> CheckSleepHealth( [NaturalLanguageParam(Description = "Date range to check, e.g. 'last week', 'this week'. Defaults to last 7 days."
                                                                    , AllowEmpty  = true)]
                                                string dateRange )
    {
        var (from, to) = ResolveDefaultDateRange(dateRange);

        try
        {
            var report = await _patternService.AnalyseAsync(from, to);

            var sleepPatterns = report.Patterns
                .Where(pattern => pattern.Name.ContainsIgnoreCase("Sleep"))
                .ToList();

            if (sleepPatterns.Count == 0)
                return "No sleep concerns detected for this period. Your rest looks healthy.";

            var concern   = sleepPatterns.FirstOrDefault(pattern => pattern.Severity == PatternSeverity.Concern);
            var attention = sleepPatterns.FirstOrDefault(pattern => pattern.Severity == PatternSeverity.Attention);

            if (concern   is not null) return concern.Description;
            if (attention is not null) return attention.Description;

            return sleepPatterns[0].Description;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            return "I wasn't able to check sleep health right now. Make sure your health data is connected, then try again.";
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (DateOnly From, DateOnly To) ResolveDefaultDateRange(string dateRange)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        if (dateRange.HasNoValue())
            return (today.AddDays(-7), today);

        var normalized = dateRange.Trim().ToLowerInvariant();

        if (normalized is "last week" or "this week" or "past week" or "past 7 days" or "last 7 days")
            return (today.AddDays(-7), today);

        if (normalized is "last 30 days" or "past 30 days" or "this month")
            return (today.AddDays(-30), today);

        if (!TaskDateParser.TryParseDate(dateRange, out var parsed))
            return (today.AddDays(-7), today);

        var singleDay = DateOnly.FromDateTime(parsed.LocalDateTime.Date);
        return (singleDay, today);
    }
}
