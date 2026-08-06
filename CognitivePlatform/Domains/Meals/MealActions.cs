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
        Description = "Logs a meal constructed via COCE."
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
        [NaturalLanguageParam(Description = "The Meal object containing foods, meal type, and timestamp."
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

        var saved = await _mealService.SaveAsync(meal).ConfigureAwait(false);
        var sb    = new StringBuilder();
        
        sb.Append($"Logged {saved.MealType} ");
        if (saved.Foods.Count == 1)
        {
            sb.Append($"({saved.Foods[0].Name}) ");
        }
        sb.Append($"for today at {saved.ConsumedAt.ToLocalTime():h:mm tt}.");

        if (saved.Foods.Count > 1)
        {
            sb.AppendLine();
            sb.Append($"Logged {saved.MealType} with {saved.Foods.Count} items:");
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

        if (dateRange.HasValue() && !TryResolveDateRange(dateRange, out from, out to))
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
            sb.Append($"## {meal.MealType} ({meal.ConsumedAt.ToLocalTime():h:mm tt}) (ID: {meal.Id})");
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
