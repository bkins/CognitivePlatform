namespace CognitivePlatform.Api.Wellbeing;

public interface IWellbeingPatternService
{
    Task<WellbeingReport> AnalyseAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
