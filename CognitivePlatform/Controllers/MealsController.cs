using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CognitivePlatform.Api.Domains.Meals;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/meals")]
public sealed class MealsController : ControllerBase
{
    private readonly IMealService _mealService;

    public MealsController(IMealService mealService)
    {
        _mealService = mealService ?? throw new ArgumentNullException(nameof(mealService));
    }

    /// <summary>
    /// Returns all meals logged for today in the active workspace.
    /// </summary>
    [HttpGet("today")]
    public async Task<ActionResult<IReadOnlyList<Meal>>> GetToday()
    {
        var today = DateTimeOffset.Now.Date;
        var meals = await _mealService.ListAsync(today, today.AddDays(1)).ConfigureAwait(false);
        return Ok(meals);
    }

    /// <summary>
    /// Returns a single meal by ID, or 404 if not found or soft-deleted.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Meal>> GetById([FromRoute] Guid id)
    {
        var meal = await _mealService.GetAsync(id).ConfigureAwait(false);

        if (meal is null || meal.IsDeleted)
            return NotFound($"Meal with ID '{id:N}' was not found.");

        return Ok(meal);
    }

    /// <summary>
    /// Returns meals within the specified date range.
    /// If no parameters are provided, defaults to today.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Meal>>> GetRange( [FromQuery] string? from = null
                                                                , [FromQuery] string? to   = null )
    {
        DateTimeOffset fromOffset;
        DateTimeOffset toOffset;

        if (string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to))
        {
            var today  = DateTimeOffset.Now.Date;
            fromOffset = today;
            toOffset   = today.AddDays(1);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                return BadRequest("Both 'from' and 'to' query parameters are required when filtering by range.");

            if (!DateTimeOffset.TryParse(from, out fromOffset) && !DateOnly.TryParse(from, out var fromDate))
                return BadRequest("'from' must be a valid date or timestamp string (e.g. yyyy-MM-dd).");

            if (!DateTimeOffset.TryParse(to, out toOffset) && !DateOnly.TryParse(to, out var toDate))
                return BadRequest("'to' must be a valid date or timestamp string (e.g. yyyy-MM-dd).");

            if (fromOffset == default && DateOnly.TryParse(from, out var parsedFromDate))
                fromOffset = new DateTimeOffset(parsedFromDate.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local.GetUtcOffset(DateTime.Now));

            if (toOffset == default && DateOnly.TryParse(to, out var parsedToDate))
                toOffset = new DateTimeOffset(parsedToDate.ToDateTime(TimeOnly.MinValue).AddDays(1), TimeZoneInfo.Local.GetUtcOffset(DateTime.Now));

            if (fromOffset > toOffset)
                return BadRequest("'from' must be on or before 'to'.");
        }

        var meals = await _mealService.ListAsync(fromOffset, toOffset).ConfigureAwait(false);
        return Ok(meals);
    }

    /// <summary>
    /// Returns a calculated nutritional summary across the specified date range.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<NutritionSummaryDto>> GetSummary( [FromQuery] string? from = null
                                                                   , [FromQuery] string? to   = null )
    {
        DateTimeOffset fromOffset = DateTimeOffset.Now.Date;
        DateTimeOffset toOffset   = fromOffset.AddDays(1);

        if (from.HasValue() && to.HasValue())
        {
            if (DateTimeOffset.TryParse(from, out var parsedFrom))
                fromOffset = parsedFrom;
            else if (DateOnly.TryParse(from, out var parsedFromDate))
                fromOffset = new DateTimeOffset(parsedFromDate.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local.GetUtcOffset(DateTime.Now));
            else
                return BadRequest("'from' must be a valid date or timestamp string.");

            if (DateTimeOffset.TryParse(to, out var parsedTo))
                toOffset = parsedTo;
            else if (DateOnly.TryParse(to, out var parsedToDate))
                toOffset = new DateTimeOffset(parsedToDate.ToDateTime(TimeOnly.MinValue).AddDays(1), TimeZoneInfo.Local.GetUtcOffset(DateTime.Now));
            else
                return BadRequest("'to' must be a valid date or timestamp string.");
        }

        var meals   = await _mealService.ListAsync(fromOffset, toOffset).ConfigureAwait(false);
        var summary = CalculateNutritionSummary(meals, fromOffset, toOffset);

        return Ok(summary);
    }

    /// <summary>
    /// Directly saves a new meal.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Meal>> Create([FromBody] Meal meal)
    {
        if (meal is null)
            return BadRequest("Meal payload cannot be null.");

        var saved = await _mealService.SaveAsync(meal).ConfigureAwait(false);
        return CreatedAtAction(nameof(GetById), new { id = saved.Id }, saved);
    }

    /// <summary>
    /// Appends food entries to an existing meal.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<Meal>> Update( [FromRoute] Guid id
                                                , [FromBody] List<FoodEntry> foodsToAdd )
    {
        if (foodsToAdd is null || foodsToAdd.Count == 0)
            return BadRequest("Foods to add list cannot be null or empty.");

        var updated = await _mealService.UpdateAsync(id, foodsToAdd).ConfigureAwait(false);

        if (updated is null)
            return NotFound($"Meal with ID '{id:N}' was not found.");

        return Ok(updated);
    }

    /// <summary>
    /// Soft-deletes a meal entry.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        var deleted = await _mealService.SoftDeleteAsync(id).ConfigureAwait(false);

        if (!deleted)
            return NotFound($"Meal with ID '{id:N}' was not found.");

        return Ok($"Meal '{id:N}' deleted.");
    }

    internal static NutritionSummaryDto CalculateNutritionSummary( IEnumerable<Meal> meals
                                                                  , DateTimeOffset   from
                                                                  , DateTimeOffset   to )
    {
        var mealList       = meals.ToList();
        var allFoods       = mealList.SelectMany(m => m.Foods).ToList();
        var enrichedFoods  = allFoods.Where(f => f.Nutrition is not null).ToList();

        var totalCalories = enrichedFoods.Sum(f => f.Nutrition?.Calories ?? 0);
        var totalProtein  = enrichedFoods.Sum(f => f.Nutrition?.ProteinGrams ?? 0);
        var totalCarbs    = enrichedFoods.Sum(f => f.Nutrition?.CarbsGrams ?? 0);
        var totalFat      = enrichedFoods.Sum(f => f.Nutrition?.FatGrams ?? 0);
        var totalFiber    = enrichedFoods.Sum(f => f.Nutrition?.FiberGrams ?? 0);

        return new NutritionSummaryDto
               {
                   FromDateUtc              = from.ToUniversalTime()
                 , ToDateUtc                = to.ToUniversalTime()
                 , TotalMeals               = mealList.Count
                 , TotalFoodItems           = allFoods.Count
                 , EnrichedFoodItemsCount   = enrichedFoods.Count
                 , TotalCalories            = Math.Round(totalCalories, 1)
                 , TotalProteinGrams        = Math.Round(totalProtein, 1)
                 , TotalCarbsGrams          = Math.Round(totalCarbs, 1)
                 , TotalFatGrams            = Math.Round(totalFat, 1)
                 , TotalFiberGrams          = Math.Round(totalFiber, 1)
               };
    }
}
