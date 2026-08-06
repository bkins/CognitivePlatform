using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Meals;

/// <summary>
/// Enriches food items with calories and macronutrient profiles via the OpenFoodFacts HTTP API.
/// Designed for resilience: network errors, missing items, or invalid payloads return null without throwing.
/// </summary>
public sealed class OpenFoodFactsNutritionProvider : INutritionLookupService
{
    private readonly HttpClient _httpClient;

    public OpenFoodFactsNutritionProvider(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<NutritionalInfo?> LookupAsync( string            foodName
                                                   , double?           quantity          = null
                                                   , string?           unit              = null
                                                   , CancellationToken cancellationToken = default )
    {
        if (foodName.HasNoValue())
            return null;

        try
        {
            var query   = Uri.EscapeDataString(foodName);
            var url     = $"cgi/search.pl?search_terms={query}&search_simple=1&action=process&json=1&page_size=1";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "CognitivePlatform-MealLogger/1.0");

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (content.HasNoValue())
                return null;

            using var doc      = JsonDocument.Parse(content);
            var       root     = doc.RootElement;
            var       products = root.TryGetProperty("products", out var prodElement) && prodElement.ValueKind == JsonValueKind.Array
                                     ? prodElement
                                     : default;

            if (products.ValueKind != JsonValueKind.Array || products.GetArrayLength() == 0)
                return null;

            var product = products[0];
            if (!product.TryGetProperty("nutriments", out var nutriments))
                return null;

            var multiplier = CalculateMultiplier(quantity, unit);

            var info = new NutritionalInfo
                       {
                           Calories     = TryGetDouble(nutriments, "energy-kcal_100g",   multiplier) ?? TryGetDouble(nutriments, "energy-kcal_value", multiplier)
                         , ProteinGrams = TryGetDouble(nutriments, "proteins_100g",      multiplier)
                         , CarbsGrams   = TryGetDouble(nutriments, "carbohydrates_100g", multiplier)
                         , FatGrams     = TryGetDouble(nutriments, "fat_100g",           multiplier)
                         , FiberGrams   = TryGetDouble(nutriments, "fiber_100g",         multiplier)
                       };

            if (info.Calories.HasValue || info.ProteinGrams.HasValue || info.CarbsGrams.HasValue || info.FatGrams.HasValue)
                return info;

            return null;
        }
        catch (Exception)
        {
            // Resilient fast path: network failures, JSON parsing bugs, or timeouts fall back to null
            return null;
        }
    }

    private static double CalculateMultiplier(double? quantity, string? unit)
    {
        if (!quantity.HasValue || quantity.Value <= 0)
            return 1.0;

        if (unit is not null && unit.HasValue() && (unit.IsEqualTo("grams") || unit.IsEqualTo("g") || unit.IsEqualTo("ml") || unit.IsEqualTo("milliliters")))
            return quantity.Value / 100.0;

        // If unit is pieces, servings, or unspecified, treat 1 unit ~ 100g standard baseline for portion scaling
        return quantity.Value;
    }

    private static double? TryGetDouble(JsonElement element, string propertyName, double multiplier)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return null;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var numValue))
            return Math.Round(numValue * multiplier, 2);

        if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), out var strValue))
            return Math.Round(strValue * multiplier, 2);

        return null;
    }
}
