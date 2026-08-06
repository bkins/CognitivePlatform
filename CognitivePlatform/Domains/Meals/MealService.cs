using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Workspace;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Meals;

public class MealService : IMealService
{
    private readonly IObjectStore      _store;
    private readonly IWorkspaceContext _workspaceContext;

    public MealService( IObjectStore      store
                      , IWorkspaceContext workspaceContext )
    {
        _store            = store;
        _workspaceContext = workspaceContext;
    }

    public async Task<Meal> SaveAsync(Meal meal)
    {
        var partitionKey = _workspaceContext.ActivePartitionKey;
        meal.ConsumedAt  = meal.ConsumedAt.ToUniversalTime();
        await _store.Save(meal, partitionKey, meal.Id).ConfigureAwait(false);
        return meal;
    }

    public async Task<Meal?> GetAsync(Guid id)
    {
        var partitionKey = _workspaceContext.ActivePartitionKey;
        return await _store.GetAsync<Meal>(id.ToString("N"), partitionKey).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Meal>> ListAsync( DateTimeOffset? fromUtc = null
                                                    , DateTimeOffset? toUtc   = null )
    {
        var partitionKey = _workspaceContext.ActivePartitionKey;
        var utcFrom      = fromUtc?.ToUniversalTime();
        var utcTo        = toUtc?.ToUniversalTime();
        var meals        = await _store.ListAsync<Meal>(partitionKey, utcFrom, utcTo).ConfigureAwait(false);
        
        return meals.Where(meal => meal.IsDeleted.Not())
                    .ToList();
    }

    public async Task<bool> SoftDeleteAsync(Guid id)
    {
        var partitionKey = _workspaceContext.ActivePartitionKey;
        var meal         = await GetAsync(id).ConfigureAwait(false);
        
        if (meal is null || meal.IsDeleted)
            return false;

        var deletedMeal = new Meal
                          {
                              Id         = meal.Id
                            , MealType   = meal.MealType
                            , ConsumedAt = meal.ConsumedAt.ToUniversalTime()
                            , Foods      = meal.Foods
                            , Notes      = meal.Notes
                            , Source     = meal.Source
                            , IsDeleted  = true
                            , DeletedUtc = DateTime.UtcNow
                          };

        await _store.Save(deletedMeal, partitionKey, meal.Id).ConfigureAwait(false);
        return true;
    }

    public async Task<Meal?> UpdateAsync( Guid            id
                                        , List<FoodEntry> foodsToAdd )
    {
        var partitionKey = _workspaceContext.ActivePartitionKey;
        var meal         = await GetAsync(id).ConfigureAwait(false);

        if (meal is null || meal.IsDeleted)
            return null;

        var updatedFoods = meal.Foods.Concat(foodsToAdd).ToList();
        var updatedMeal  = new Meal
                           {
                               Id         = meal.Id
                             , MealType   = meal.MealType
                             , ConsumedAt = meal.ConsumedAt.ToUniversalTime()
                             , Foods      = updatedFoods
                             , Notes      = meal.Notes
                             , Source     = meal.Source
                             , IsDeleted  = meal.IsDeleted
                             , DeletedUtc = meal.DeletedUtc
                           };

        await _store.Save(updatedMeal, partitionKey, meal.Id).ConfigureAwait(false);
        return updatedMeal;
    }
}
