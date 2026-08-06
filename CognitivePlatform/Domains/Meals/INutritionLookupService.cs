using System.Threading;
using System.Threading.Tasks;

namespace CognitivePlatform.Api.Domains.Meals;

/// <summary>
/// Proactively resolves nutritional facts (calories, macros, and micronutrients)
/// for a specified food item using external catalogs or internal estimations.
/// </summary>
public interface INutritionLookupService
{
    /// <summary>
    /// Looks up nutritional facts for a food item.
    /// Returns null if the item cannot be found or if external queries fail or time out.
    /// </summary>
    Task<NutritionalInfo?> LookupAsync( string            foodName
                                      , double?           quantity          = null
                                      , string?           unit              = null
                                      , CancellationToken cancellationToken = default );
}
