namespace CognitivePlatform.Api.Avails.Extensions;

[Obsolete("Use CP.Shared.Primitives.Avails.Extensions instead")]
public static class LlmJsonExtensions
{
    public static string NormalizeLlmJson(this string raw)
    {
        if (raw.DoesNotHaveValueOrIsNullOrEmpty()) return raw;

        var text = raw.Trim();

        // Restore missing braces (common LLM failure mode)
        if (text.DoesNotStartWithOrdinal("{")) text =  "{\n" + text;
        if (text.DoesNotEndWith("}"))          text += "\n}";

        return text;
    }
}
