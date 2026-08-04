using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CognitivePlatform.Api.Domains.Meals;

public interface IMealService
{
    Task<Meal> SaveAsync(Meal meal);
    Task<Meal?> GetAsync(Guid id);
    Task<IReadOnlyList<Meal>> ListAsync(DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null);
    Task<bool> SoftDeleteAsync(Guid id);
    Task<Meal?> UpdateAsync(Guid id, List<FoodEntry> foodsToAdd);
}
