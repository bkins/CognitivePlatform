using System.Net;
using System.Net.Http;
using Moq;
using Moq.Protected;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Integrations.Calendar;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Tests;

public class GoogleCalendarProviderTests
{
    // ================================================================
    // GetCalendarListAsync — auth failure handling
    // ================================================================

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetCalendarListAsync_ThrowsCalendarAuthException_WhenApiReturnsAuthError(HttpStatusCode statusCode)
    {
        var provider = BuildProvider(calendarListStatusCode: statusCode);

        await Assert.ThrowsAsync<CalendarAuthException>(() => provider.GetCalendarListAsync());
    }

    [Fact]
    public async Task GetCalendarListAsync_FallsBackToPrimary_WhenApiReturnsNonAuthError()
    {
        var provider = BuildProvider(calendarListStatusCode: HttpStatusCode.ServiceUnavailable);

        var result = await provider.GetCalendarListAsync();

        Assert.Single(result);
        Assert.Equal("Primary", result[0].Name);
    }

    // ================================================================
    // Helpers
    // ================================================================

    private static GoogleCalendarProvider BuildProvider(HttpStatusCode calendarListStatusCode)
    {
        var storeMock = new Mock<IObjectStore>();
        storeMock.Setup(store => store.Get<CalendarTokens>("default", It.IsAny<string?>()))
                 .Returns(new CalendarTokens
                          {
                                  AccessToken = "fake-access-token"
                                , ExpiresAt   = DateTimeOffset.UtcNow.AddHours(1)
                          });

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
                   .Setup<Task<HttpResponseMessage>>("SendAsync"
                                                   , ItExpr.IsAny<HttpRequestMessage>()
                                                   , ItExpr.IsAny<CancellationToken>())
                   .ReturnsAsync(new HttpResponseMessage(calendarListStatusCode));

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(factory => factory.CreateClient(It.IsAny<string>()))
                   .Returns(new HttpClient(handlerMock.Object));

        var settings = Options.Create(new GoogleCalendarSettings
                                      {
                                              ClientId               = "client-id"
                                            , ClientSecret           = "client-secret"
                                            , RedirectUri            = "http://localhost/callback"
                                            , TokenStorePartitionKey = "calendar-tokens"
                                      });

        return new GoogleCalendarProvider( settings
                                         , storeMock.Object
                                         , httpFactory.Object
                                         , Mock.Of<ILogger<GoogleCalendarProvider>>() );
    }
}
