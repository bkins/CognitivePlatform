using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.KnowledgeInbox;
using CognitivePlatform.Api.KnowledgeInbox.Interfaces;

namespace CognitivePlatform.Api.Domains.Meals;

/// <summary>
/// Knowledge source for logged meals. Adapts meal domain objects into KnowledgeItemDto.
/// </summary>
public sealed class MealKnowledgeSource : IKnowledgeSource
{
    private readonly IMealService _mealService;
    private readonly IObjectStore _objectStore;

    public KnowledgeKind Kind => KnowledgeKind.Meal;

    public MealKnowledgeSource( IMealService mealService
                              , IObjectStore objectStore )
    {
        _mealService = mealService ?? throw new ArgumentNullException(nameof(mealService));
        _objectStore = objectStore ?? throw new ArgumentNullException(nameof(objectStore));
    }

    public IEnumerable<KnowledgeItemDto> GetKnowledgeItems( KnowledgeQuery    query
                                                          , CancellationToken ct )
    {
        var meals = _objectStore.List<Meal>(partitionKey: null);

        foreach (var meal in meals)
        {
            if (query.Id is not null && meal.Id != query.Id.Value.ToString("N"))
                continue;

            var allFoods  = meal.Foods ?? new List<FoodEntry>();
            var foodNames = allFoods.Select(food => food.Name).ToList();

            yield return new KnowledgeItemDto
                         {
                             Id             = Guid.Parse(meal.Id)
                           , Kind           = KnowledgeKind.Meal
                           , Title          = DeriveTitle(meal)
                           , Summary        = DeriveSummary(meal)
                           , CreatedAt      = meal.ConsumedAt
                           , LastModifiedAt = meal.ConsumedAt
                           , Status         = meal.IsDeleted ? KnowledgeStatus.Deleted : KnowledgeStatus.Active
                           , Tags           = foodNames
                           , Importance     = null
                           , Urgency        = null
                         };
        }
    }

    public IReadOnlyList<ObjectHeader> ListHeaders( DateTimeOffset? fromUtc
                                                  , DateTimeOffset? toUtc )
    {
        return _objectStore.List<Meal>(partitionKey: null, fromUtc: fromUtc, toUtc: toUtc)
                           .Where(meal => !meal.IsDeleted)
                           .Select(meal => new ObjectHeader(
                                       meal.Id
                                     , KnowledgeKind.Meal.ToString()
                                     , meal.ConsumedAt
                                     , meal.ConsumedAt))
                           .ToList();
    }

    public void Archive(Guid id, CancellationToken ct)
    {
        _objectStore.SoftDelete<Meal>(id.ToString("N"));
    }

    private static string DeriveTitle(Meal meal)
    {
        var type       = meal.MealType.ToString();
        var date       = meal.ConsumedAt.ToLocalTime().ToString("yyyy-MM-dd");
        var itemsCount = meal.Foods?.Count ?? 0;
        return $"{type} ({date}) — {itemsCount} item(s)";
    }

    private static string? DeriveSummary(Meal meal)
    {
        if (meal.Foods is null || meal.Foods.Count == 0)
            return null;

        var items = string.Join(", ", meal.Foods.Select(food => food.Name));
        return items.Length <= 140
                   ? items
                   : string.Concat(items.AsSpan(0, 137), "…");
    }
}
