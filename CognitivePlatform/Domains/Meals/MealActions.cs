using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Registry.Domains;
using CP.Shared.Primitives.Avails;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Meals;

[Domain(typeof(FoodDomain))]
public class MealActions
{
    private readonly IMealService _mealService;

    public MealActions(IMealService mealService)
    {
        _mealService = mealService ?? throw new ArgumentNullException(nameof(mealService));
    }

    [NaturalLanguageAction(
        Description = "Logs a SINGLE meal constructed via COCE. Use LogMeals (plural) when the user mentions more than one meal type in the same message."
      , Examples    =
        [
            "For breakfast I had two scrambled eggs."
          , "I had pizza for lunch around 1:30."
          , "Log snack: a handful of almonds."
        ]
      , Category    = "Food"
      , AllowsClarification = true
      , IsReplayable = true)]
    public async Task<ActionResult> LogMeal(
        [NaturalLanguageParam(Description = "The Meal object containing foods, meal type, and timestamp. Omit consumedAt unless the user explicitly stated a time."
                            , Optional    = false
                            , AllowEmpty  = false)]
        Meal meal)
    {
        if (meal is null)
        {
            return new ActionResult
                   {
                       Success = false
                     , Message = "No meal details could be parsed."
                   };
        }

        return await ProcessMealAsync(meal).ConfigureAwait(false);
    }

    [NaturalLanguageAction(
        Description = "Logs MULTIPLE meals from a single message (e.g. lunch AND dinner mentioned together). Pass each meal as a separate object in the array."
      , Examples    =
        [
            "For lunch I had a turkey sandwich, and for dinner I had grilled salmon and rice."
          , "I had eggs for breakfast and a salad for lunch."
          , "Breakfast was oatmeal, lunch was soup, dinner was chicken."
        ]
      , Category    = "Food"
      , IsReplayable = true)]
    public async Task<ActionResult> LogMeals(
        [NaturalLanguageParam(Description = "Array of Meal objects — one per meal type. Each contains Foods, MealType, and optionally ConsumedAt."
                            , Optional    = false
                            , AllowEmpty  = false)]
        List<Meal> meals)
    {
        if (meals is null || meals.Count == 0)
        {
            return new ActionResult
                   {
                       Success = false
                     , Message = "No meal details could be parsed."
                   };
        }

        var sb      = new StringBuilder();
        var results = new List<Meal>();

        foreach (var meal in meals)
        {
            var result = await ProcessMealAsync(meal).ConfigureAwait(false);
            if (sb.Length > 0)
                sb.AppendLine();
            sb.Append(result.Message);

            if (result.Data is Meal savedMeal)
                results.Add(savedMeal);
        }

        return new ActionResult
               {
                   Success = true
                 , Message = sb.ToString()
                 , Data    = results
               };
    }

    [NaturalLanguageAction(
        Description = "Lists meals logged for a given date range."
      , Examples    =
        [
            "What did I eat today?"
          , "List meals from yesterday."
          , "show my food log for this week"
        ]
      , Category    = "Food")]
    public async Task<ActionResult> ListMeals(
        [NaturalLanguageParam(Description = "Date or period to query, e.g. 'yesterday', 'today', 'last week'."
                            , Optional    = true
                            , AllowEmpty  = true)]
        string? dateRange = null)
    {
        DateTimeOffset from = DateTimeOffset.Now.Date;
        DateTimeOffset to   = from.AddDays(1);

        if (dateRange.HasValue() && !TryResolveDateRange(dateRange!, out from, out to))
        {
            return new ActionResult
                   {
                       Success = false
                     , Message = $"I couldn't parse '{dateRange}' as a date or period. Try 'today', 'yesterday', or 'last week'."
                   };
        }

        var meals = await _mealService.ListAsync(from, to).ConfigureAwait(false);

        if (meals.Count == 0)
        {
            return new ActionResult
                   {
                       Success = true
                     , Message = $"No meals logged for {FormatDateRange(from, to)}."
                   };
        }

        var sb = new StringBuilder();
        sb.Append($"# Food Log ({FormatDateRange(from, to)})");
        
        foreach (var meal in meals.OrderBy(m => m.ConsumedAt))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append($"## {meal.MealType} ({meal.ConsumedAt.ToLocalTime():h:mm tt})");
            if (meal.Notes.HasValue())
            {
                sb.AppendLine();
                sb.Append($"*Notes: {meal.Notes}*");
            }
            foreach (var food in meal.Foods)
            {
                sb.AppendLine();
                sb.Append($"- {FormatFood(food)}");
            }
        }

        return new ActionResult
               {
                   Success = true
                 , Message = sb.ToString()
                 , Data    = meals
               };
    }

    [NaturalLanguageAction(
        Description = "Soft-deletes a meal entry."
      , Examples    =
        [
            "delete meal breakfast"
          , "Remove lunch entry"
          , "delete meal 9a7b9c8d-6e5f-4a3b-2c1d-0e9f8a7b6c5d"
        ]
      , Category    = "Food")]
    [DestructiveAction]
    public async Task<ActionResult> DeleteMeal(
        [NaturalLanguageParam(Description = "The meal GUID or meal type (e.g. breakfast) to delete."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string mealIdOrType)
    {
        Guid mealId;
        Meal? targetMeal = null;

        if (Guid.TryParse(mealIdOrType, out mealId))
        {
            targetMeal = await _mealService.GetAsync(mealId).ConfigureAwait(false);
        }
        else if (Enum.TryParse<MealType>(mealIdOrType, ignoreCase: true, out var mealType))
        {
            var todayMeals = await _mealService.ListAsync(DateTimeOffset.Now.Date, DateTimeOffset.Now.Date.AddDays(1)).ConfigureAwait(false);
            targetMeal     = todayMeals.FirstOrDefault(m => m.MealType == mealType);
        }

        if (targetMeal is null || targetMeal.IsDeleted)
        {
            return new ActionResult
                   {
                       Success = false
                     , Message = $"No meal matching '{mealIdOrType}' found for today."
                   };
        }

        var deleted = await _mealService.SoftDeleteAsync(Guid.Parse(targetMeal.Id)).ConfigureAwait(false);

        if (!deleted)
        {
            return new ActionResult
                   {
                       Success = false
                     , Message = $"Failed to delete meal '{targetMeal.MealType}'."
                   };
        }

        return new ActionResult
               {
                   Success = true
                 , Message = $"Deleted {targetMeal.MealType} entry (ID: {Guid.Parse(targetMeal.Id):N})."
               };
    }

    [NaturalLanguageAction(
        Description = "Updates or appends food items to an existing meal."
      , Examples    =
        [
            "add coffee to my breakfast"
          , "update lunch: add 1 apple"
        ]
      , Category    = "Food")]
    public async Task<ActionResult> UpdateMeal(
        [NaturalLanguageParam(Description = "The meal GUID or meal type (e.g. breakfast) to update."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string mealIdOrType
      , [NaturalLanguageParam(Description = "The list of FoodEntry items to add."
                            , Optional    = false
                            , AllowEmpty  = false)]
        List<FoodEntry> foodsToAdd)
    {
        Guid mealId;
        Meal? targetMeal = null;

        if (Guid.TryParse(mealIdOrType, out mealId))
        {
            targetMeal = await _mealService.GetAsync(mealId).ConfigureAwait(false);
        }
        else if (Enum.TryParse<MealType>(mealIdOrType, ignoreCase: true, out var mealType))
        {
            var todayMeals = await _mealService.ListAsync(DateTimeOffset.Now.Date, DateTimeOffset.Now.Date.AddDays(1)).ConfigureAwait(false);
            targetMeal     = todayMeals.FirstOrDefault(m => m.MealType == mealType);
        }

        if (targetMeal is null || targetMeal.IsDeleted)
        {
            return new ActionResult
                   {
                       Success = false
                     , Message = $"No meal matching '{mealIdOrType}' found for today."
                   };
        }

        var updated = await _mealService.UpdateAsync(Guid.Parse(targetMeal.Id), foodsToAdd).ConfigureAwait(false);

        if (updated is null)
        {
            return new ActionResult
                   {
                       Success = false
                     , Message = $"Failed to update meal '{targetMeal.MealType}'."
                   };
        }

        var sb = new StringBuilder();
        sb.Append($"Updated {targetMeal.MealType} by adding {foodsToAdd.Count} item(s):");
        foreach (var food in foodsToAdd)
        {
            sb.AppendLine();
            sb.Append($"- {FormatFood(food)}");
        }

        return new ActionResult
               {
                   Success = true
                 , Message = sb.ToString()
                 , Data    = updated
               };
    }

    [NaturalLanguageAction(
        Description = "Summarizes nutritional intake (calories, protein, carbs, fat, fiber) for a given date range."
      , Examples    =
        [
            "Show my nutrition summary for today"
          , "Macro summary for yesterday"
          , "How many calories have I eaten today?"
          , "Show my macros for this week"
        ]
      , Category    = "Food")]
    [FastPath]
    public async Task<ActionResult> GetNutritionSummary(
        [NaturalLanguageParam(Description = "Date or period to summarize, e.g. 'today', 'yesterday', 'this week', 'last week'."
                            , Optional    = true
                            , AllowEmpty  = true)]
        string? dateRange = null)
    {
        DateTimeOffset from = DateTimeOffset.Now.Date;
        DateTimeOffset to   = from.AddDays(1);

        if (dateRange.HasValue() && !TryResolveDateRange(dateRange!, out from, out to))
        {
            return new ActionResult
                   {
                       Success = false
                     , Message = $"I couldn't parse '{dateRange}' as a date or period. Try 'today', 'yesterday', or 'last week'."
                   };
        }

        var meals = await _mealService.ListAsync(from, to).ConfigureAwait(false);

        if (meals.Count == 0)
        {
            return new ActionResult
                   {
                       Success = true
                     , Message = $"No meals logged for {FormatDateRange(from, to)}. Log your meals to track nutritional intake."
                   };
        }

        var allFoods      = meals.SelectMany(m => m.Foods).ToList();
        var enrichedFoods = allFoods.Where(f => f.Nutrition is not null).ToList();

        var totalCalories = enrichedFoods.Sum(f => f.Nutrition?.Calories ?? 0);
        var totalProtein  = enrichedFoods.Sum(f => f.Nutrition?.ProteinGrams ?? 0);
        var totalCarbs    = enrichedFoods.Sum(f => f.Nutrition?.CarbsGrams ?? 0);
        var totalFat      = enrichedFoods.Sum(f => f.Nutrition?.FatGrams ?? 0);
        var totalFiber    = enrichedFoods.Sum(f => f.Nutrition?.FiberGrams ?? 0);

        var sb = new StringBuilder();
        sb.AppendLine($"# Nutrition Summary ({FormatDateRange(from, to)})");
        sb.AppendLine($"*Based on {enrichedFoods.Count} of {allFoods.Count} food item(s) with nutrition facts across {meals.Count} meal(s).*");
        sb.AppendLine();
        sb.AppendLine("| Metric | Total |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| **Calories** | {totalCalories:F0} kcal |");
        sb.AppendLine($"| **Protein** | {totalProtein:F1} g |");
        sb.AppendLine($"| **Carbohydrates** | {totalCarbs:F1} g |");
        sb.AppendLine($"| **Fat** | {totalFat:F1} g |");
        sb.Append($"| **Fiber** | {totalFiber:F1} g |");

        var summaryData = new NutritionSummaryDto
                          {
                              FromDateUtc            = from.ToUniversalTime()
                            , ToDateUtc              = to.ToUniversalTime()
                            , TotalMeals             = meals.Count
                            , TotalFoodItems         = allFoods.Count
                            , EnrichedFoodItemsCount = enrichedFoods.Count
                            , TotalCalories          = Math.Round(totalCalories, 1)
                            , TotalProteinGrams      = Math.Round(totalProtein, 1)
                            , TotalCarbsGrams        = Math.Round(totalCarbs, 1)
                            , TotalFatGrams          = Math.Round(totalFat, 1)
                            , TotalFiberGrams        = Math.Round(totalFiber, 1)
                          };

        return new ActionResult
               {
                   Success = true
                 , Message = sb.ToString()
                 , Data    = summaryData
               };
    }

    // -----------------------------------------------------------------------
    // Core Processing
    // -----------------------------------------------------------------------

    /// <summary>
    /// Core meal-logging logic shared by <see cref="LogMeal"/> and <see cref="LogMeals"/>.
    /// Handles duplicate detection, appending to an existing meal, and fresh saves.
    /// </summary>
    private async Task<ActionResult> ProcessMealAsync(Meal meal)
    {
        var day           = meal.ConsumedAt.ToLocalTime().Date;
        var offset        = TimeZoneInfo.Local.GetUtcOffset(day);
        var from          = new DateTimeOffset(day,            offset);
        var to            = new DateTimeOffset(day.AddDays(1), offset);
        var existingMeals = await _mealService.ListAsync(from, to).ConfigureAwait(false);

        if (existingMeals is not null && existingMeals.Count > 0)
        {
            var existingMeal = existingMeals.FirstOrDefault(existing => existing.MealType == meal.MealType
                                                                      && meal.MealType != MealType.Unspecified);

            if (existingMeal is not null)
            {
                var newFoods = meal.Foods.Where(incoming =>
                                                    !existingMeal.Foods.Any(existing =>
                                                        existing.Name.EqualsIgnoreCase(incoming.Name)))
                                         .ToList();

                if (newFoods.Count == 0)
                {
                    return new ActionResult
                           {
                               Success = true
                             , Message = $"An entry for {existingMeal.MealType} containing these items is already logged for today (no duplicate added)."
                             , Data    = existingMeal
                           };
                }

                if (meal.MealType is MealType.Breakfast or MealType.Lunch or MealType.Dinner)
                {
                    var updated = await _mealService.UpdateAsync(Guid.Parse(existingMeal.Id), newFoods).ConfigureAwait(false);
                    var mergeSb = new StringBuilder();

                    mergeSb.Append($"Added {newFoods.Count} new item(s) to today's {existingMeal.MealType} entry:");
                    foreach (var food in newFoods)
                    {
                        mergeSb.AppendLine();
                        mergeSb.Append($"- {FormatFood(food)}");
                    }

                    return new ActionResult
                           {
                               Success = true
                             , Message = mergeSb.ToString()
                             , Data    = updated
                           };
                }
            }
        }

        var saved = await _mealService.SaveAsync(meal).ConfigureAwait(false);
        var sb    = new StringBuilder();

        sb.Append($"Logged {saved.MealType} ");

        if (saved.Foods.Count == 1)
        {
            sb.Append($"({FormatFood(saved.Foods[0])}) for today at {saved.ConsumedAt.ToLocalTime():h:mm tt}.");
        }
        else
        {
            sb.Append($"with {saved.Foods.Count} items for today at {saved.ConsumedAt.ToLocalTime():h:mm tt}:");
            foreach (var food in saved.Foods)
            {
                sb.AppendLine();
                sb.Append($"- {FormatFood(food)}");
            }
        }

        return new ActionResult
               {
                   Success = true
                 , Message = sb.ToString()
                 , Data    = saved
               };
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string FormatFood(FoodEntry food)
    {
        var sb = new StringBuilder();
        
        if (food.Quantity.HasValue)
        {
            sb.Append($"{food.Quantity:G2}x ");
        }

        sb.Append(food.Name);

        var details = new List<string>();
        if (food.Preparation.HasValue())
        {
            details.Add(food.Preparation!);
        }
        if (food.Brand.HasValue())
        {
            details.Add($"Brand: {food.Brand}");
        }
        if (food.Additions.Count > 0)
        {
            details.Add($"Additions: {string.Join(", ", food.Additions)}");
        }

        if (details.Count > 0)
        {
            sb.Append($" ({string.Join(", ", details)})");
        }

        return sb.ToString();
    }

    private static bool TryResolveDateRange( string            dateRange
                                           , out DateTimeOffset from
                                           , out DateTimeOffset to )
    {
        from = default;
        to   = default;

        var normalized  = dateRange.Trim().ToLowerInvariant();
        var today       = DateTimeOffset.Now.Date;
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(today);

        if (normalized is "last week" or "this week" or "past week" or "past 7 days")
        {
            from = new DateTimeOffset(today.AddDays(-7), localOffset);
            to   = new DateTimeOffset(today.AddDays(1),  localOffset);
            return true;
        }

        if (!TaskDateParser.TryParseDate(dateRange, out var parsed)) return false;

        var day    = parsed.LocalDateTime.Date;
        var offset = TimeZoneInfo.Local.GetUtcOffset(day);
        from = new DateTimeOffset(day,            offset);
        to   = new DateTimeOffset(day.AddDays(1), offset);
        return true;
    }

    private static string FormatDateRange(DateTimeOffset from, DateTimeOffset to)
    {
        var days = (to - from).TotalDays;
        return days <= 1
                   ? from.LocalDateTime.Date.ToString("yyyy-MM-dd")
                   : $"{from.LocalDateTime.Date:yyyy-MM-dd} – {to.LocalDateTime.Date.AddDays(-1):yyyy-MM-dd}";
    }
}
