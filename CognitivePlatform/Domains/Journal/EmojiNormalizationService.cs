namespace CognitivePlatform.Api.Domains.Journal;

public static class EmojiNormalizationService
{
    public static string MapValenceEmoji(int? moodScore)
    {
        if (moodScore is null) return "❓";

        return moodScore.Value switch
        {
            <= 1 => "😢",
            2    => "🙁",
            3    => "😐",
            4    => "🙂",
            _    => "😄"
        };
    }

    public static string MapAffectEmoji(string? mood)
    {
        if (string.IsNullOrWhiteSpace(mood)) return "❓";

        var normalized = mood.Trim().ToLowerInvariant();

        // Check for specific sub-string matches to handle descriptive modifiers (e.g. "very angry")
        if (normalized.Contains("angry") || normalized.Contains("mad"))
            return "😡";

        if (normalized.Contains("furious") || normalized.Contains("livid") || normalized.Contains("enraged") || normalized.Contains("rage"))
            return "🤬";

        if (normalized.Contains("frustrated"))
            return "😤";

        if (normalized.Contains("anxious") || normalized.Contains("nervous") || normalized.Contains("worried"))
            return "😰";

        if (normalized.Contains("stressed") || normalized.Contains("overwhelmed"))
            return "😖";

        if (normalized.Contains("sad") || normalized.Contains("unhappy") || normalized.Contains("crying"))
            return "😢";

        if (normalized.Contains("depressed") || normalized.Contains("down") || normalized.Contains("gloomy"))
            return "😞";

        if (normalized.Contains("lonely"))
            return "😔";

        if (normalized.Contains("disappointed"))
            return "😕";

        if (normalized.Contains("meh") || normalized.Contains("numb") || normalized.Contains("bored") || normalized.Contains("flat"))
            return "😐";

        if (normalized.Contains("confused"))
            return "😕";

        if (normalized.Contains("content") || normalized.Contains("satisfied"))
            return "🙂";

        if (normalized.Contains("calm") || normalized.Contains("relaxed") || normalized.Contains("peaceful"))
            return "😌";

        if (normalized.Contains("relieved"))
            return "😮‍💨";

        if (normalized.Contains("happy") || normalized.Contains("glad") || normalized.Contains("cheerful"))
            return "😄";

        if (normalized.Contains("excited") || normalized.Contains("thrilled") || normalized.Contains("hyped"))
            return "🤩";

        if (normalized.Contains("proud"))
            return "😊";

        return "❓";
    }
}
