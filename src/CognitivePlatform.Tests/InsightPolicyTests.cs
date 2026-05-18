using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Tests;

public class InsightPolicyTests
{
    [Fact]
    public void GetRepeatWindow_ReturnsCategoryOverride_WhenPresent()
    {
        var policy = new InsightPolicy
                     {
                             RepeatWindow           = TimeSpan.FromHours(48)
                           , CategoryRepeatWindows  = new Dictionary<InsightCategory, TimeSpan>
                                                      {
                                                              [InsightCategory.Journal] = TimeSpan.FromHours(24)
                                                      }
                     };

        var window = policy.GetRepeatWindow(InsightCategory.Journal);

        Assert.Equal(TimeSpan.FromHours(24), window);
    }

    [Fact]
    public void GetRepeatWindow_FallsBackToDefault_WhenNoCategoryOverride()
    {
        var policy = new InsightPolicy
                     {
                             RepeatWindow          = TimeSpan.FromHours(48)
                           , CategoryRepeatWindows = new Dictionary<InsightCategory, TimeSpan>()
                     };

        var window = policy.GetRepeatWindow(InsightCategory.Tasks);

        Assert.Equal(TimeSpan.FromHours(48), window);
    }
}
