using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Domains.Meals;
using Moq;
using Moq.Protected;
using Xunit;

namespace CognitivePlatform.Tests;

public class OpenFoodFactsNutritionProviderTests
{
    private const string SampleSuccessJson = """
                                             {
                                               "count": 1,
                                               "products": [
                                                 {
                                                   "product_name": "Greek Yogurt",
                                                   "nutriments": {
                                                     "energy-kcal_100g": 130,
                                                     "proteins_100g": 10.5,
                                                     "carbohydrates_100g": 12.0,
                                                     "fat_100g": 4.0,
                                                     "fiber_100g": 0.5
                                                   }
                                                 }
                                               ]
                                             }
                                             """;

    private const string EmptyProductsJson = """
                                             {
                                               "count": 0,
                                               "products": []
                                             }
                                             """;

    private OpenFoodFactsNutritionProvider CreateProvider(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
                     {
                         BaseAddress = new Uri("https://world.openfoodfacts.org/")
                     };

        return new OpenFoodFactsNutritionProvider(client);
    }

    [Fact]
    public async Task LookupAsync_ReturnsNutritionalInfo_WhenProductFound()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock.Protected()
                   .Setup<Task<HttpResponseMessage>>( "SendAsync"
                                                    , ItExpr.IsAny<HttpRequestMessage>()
                                                    , ItExpr.IsAny<CancellationToken>() )
                   .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                                 {
                                     Content = new StringContent(SampleSuccessJson)
                                 });

        var provider = CreateProvider(handlerMock.Object);

        var result = await provider.LookupAsync("Greek Yogurt", 100, "grams");

        Assert.NotNull(result);
        Assert.Equal(130,  result.Calories);
        Assert.Equal(10.5, result.ProteinGrams);
        Assert.Equal(12.0, result.CarbsGrams);
        Assert.Equal(4.0,  result.FatGrams);
        Assert.Equal(0.5,  result.FiberGrams);
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_WhenNoProductsFound()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock.Protected()
                   .Setup<Task<HttpResponseMessage>>( "SendAsync"
                                                    , ItExpr.IsAny<HttpRequestMessage>()
                                                    , ItExpr.IsAny<CancellationToken>() )
                   .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                                 {
                                     Content = new StringContent(EmptyProductsJson)
                                 });

        var provider = CreateProvider(handlerMock.Object);

        var result = await provider.LookupAsync("NonExistentFoodItem");

        Assert.Null(result);
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_WhenHttpErrorOccurs()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock.Protected()
                   .Setup<Task<HttpResponseMessage>>( "SendAsync"
                                                    , ItExpr.IsAny<HttpRequestMessage>()
                                                    , ItExpr.IsAny<CancellationToken>() )
                   .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var provider = CreateProvider(handlerMock.Object);

        var result = await provider.LookupAsync("Greek Yogurt");

        Assert.Null(result);
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_WhenNetworkExceptionIsThrown()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock.Protected()
                   .Setup<Task<HttpResponseMessage>>( "SendAsync"
                                                    , ItExpr.IsAny<HttpRequestMessage>()
                                                    , ItExpr.IsAny<CancellationToken>() )
                   .ThrowsAsync(new HttpRequestException("Network timeout"));

        var provider = CreateProvider(handlerMock.Object);

        var result = await provider.LookupAsync("Greek Yogurt");

        Assert.Null(result);
    }
}
