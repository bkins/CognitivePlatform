using System;
using System.Collections.Generic;
using System.Linq;
using CognitivePlatform.Api.Domains.Meals;

namespace CognitivePlatform.Api.COCE;

public sealed class MealValidator : IObjectValidator<Meal>
{
    public bool CanValidate(Type targetType)
    {
        return targetType == typeof(Meal);
    }

    public ObjectValidationResult Validate(object target)
    {
        if (target is not Meal meal)
        {
            return ObjectValidationResult.Failure(
                new[] { $"Expected target of type '{nameof(Meal)}', but received '{target?.GetType().Name ?? "null"}'." });
        }

        return Validate(meal);
    }

    public ObjectValidationResult Validate(Meal meal)
    {
        var errors            = new List<string>();
        var missingProperties = new List<string>();

        if (meal.Foods is null || meal.Foods.Count == 0)
        {
            errors.Add("Meal must contain at least one food item.");
            missingProperties.Add(nameof(Meal.Foods));
        }
        else
        {
            for (var index = 0; index < meal.Foods.Count; index++)
            {
                var food = meal.Foods[index];
                if (food is null || string.IsNullOrWhiteSpace(food.Name))
                {
                    errors.Add($"Food item at index {index} is missing a valid name.");
                    missingProperties.Add($"{nameof(Meal.Foods)}[{index}].{nameof(FoodEntry.Name)}");
                }
            }
        }

        return errors.Count == 0
                   ? ObjectValidationResult.Success()
                   : ObjectValidationResult.Failure(errors, missingProperties);
    }
}
