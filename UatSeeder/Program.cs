using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Meals;
using CognitivePlatform.Api.Domains.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        var dbPath = @"C:\CP\Data\Development\platform.db";
        var connStr = $"Data Source={dbPath}";
        
        using (var connection = new System.Data.SQLite.SQLiteConnection(connStr))
        {
            await connection.OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Objects";
            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine("Cleared Objects table.");
        }

        var store = new SqliteObjectStore(connStr);
        var today = DateTimeOffset.Now;
        var offset = TimeSpan.Zero;
        
        for (int i = 1; i <= 6; i++)
        {
            var date = new DateTimeOffset(today.Date.AddDays(-i), offset);

            var caffeineMeal = new Meal
            {
                Id = Guid.NewGuid().ToString("N"),
                MealType = MealType.Snack,
                ConsumedAt = date.AddHours(16),
                Foods = new List<FoodEntry>
                {
                    new FoodEntry { Id = Guid.NewGuid(), Name = "Coffee", Quantity = 1, Unit = "Cup" }
                },
                Source = "Seed"
            };
            await store.Save(caffeineMeal, "meals");

            var sugarMeal = new Meal
            {
                Id = Guid.NewGuid().ToString("N"),
                MealType = MealType.Dinner,
                ConsumedAt = date.AddHours(19),
                Foods = new List<FoodEntry>
                {
                    new FoodEntry { Id = Guid.NewGuid(), Name = "Ice Cream", Quantity = 1, Unit = "Bowl" }
                },
                Source = "Seed"
            };
            await store.Save(sugarMeal, "meals");

            var entryId = Guid.NewGuid().ToString("N");
            var journalEntry = new JournalEntry
            {
                Id = entryId,
                CreatedUtc = date.AddHours(21).ToUniversalTime()
            };
            await store.Save(journalEntry, "journal");

            var revision = new JournalRevision
            {
                RevisionId = Guid.NewGuid().ToString("N"),
                EntryId = entryId,
                CreatedUtc = date.AddHours(21).ToUniversalTime(),
                Text = "Feeling so exhausted today.",
                Mood = i % 2 == 0 ? "Tired" : "Stressed"
            };
            await store.Save(revision, $"journal_rev_{entryId}");

            var proteinMeal = new Meal
            {
                Id = Guid.NewGuid().ToString("N"),
                MealType = MealType.Lunch,
                ConsumedAt = date.AddHours(12),
                Foods = new List<FoodEntry>
                {
                    new FoodEntry { Id = Guid.NewGuid(), Name = "Chicken Breast", Nutrition = new NutritionalInfo { ProteinGrams = 60 } }
                },
                Source = "Seed"
            };
            await store.Save(proteinMeal, "meals");

            for (int t = 1; t <= 6; t++)
            {
                var task = new TaskItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ShortDescription = $"Test Task {t} for {date:yyyy-MM-dd}",
                    CompletedAt = date.AddHours(14).ToUniversalTime(),
                    CreatedAt = date.AddHours(8).ToUniversalTime()
                };
                await store.Save(task, "tasks");
            }
        }
        
        Console.WriteLine("Seed complete!");
    }
}
