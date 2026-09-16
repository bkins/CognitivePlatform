using System.Net;
using System.Text.RegularExpressions;

namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed partial class TtrContentNormalizer
{
    public TtrContentNormalizationResult Normalize(string? title, string? body)
    {
        var inlineMedia = new List<TtrInlineMedia>();
        var warnings    = new List<string>();
        var normalizedBody = LooksLikeHtml(body)
            ? ConvertHtml(body ?? string.Empty, inlineMedia, warnings)
            : NormalizeWhitespace(body ?? string.Empty);
        var normalizedTitle = NormalizeWhitespace(WebUtility.HtmlDecode(title ?? string.Empty));

        var markdown = normalizedTitle.Length switch
                       {
                           > 0 when normalizedBody.Length > 0 => $"# {normalizedTitle}\n\n{normalizedBody}"
                         , > 0                                => $"# {normalizedTitle}"
                         , _                                  => normalizedBody
                       };

        return new TtrContentNormalizationResult
               {
                   Markdown    = markdown
                 , InlineMedia = inlineMedia
                 , Warnings    = warnings.Distinct(StringComparer.Ordinal).ToArray()
               };
    }

    private static bool LooksLikeHtml(string? text)
    {
        return text is not null && HtmlTagRegex().IsMatch(text);
    }

    private static string ConvertHtml(string html, List<TtrInlineMedia> inlineMedia, List<string> warnings)
    {
        var text = DataImageRegex().Replace(html, match =>
        {
            try
            {
                inlineMedia.Add(new TtrInlineMedia
                                {
                                    ContentType = match.Groups[1].Value.ToLowerInvariant()
                                  , Bytes       = Convert.FromBase64String(WhitespaceRegex().Replace(match.Groups[2].Value, string.Empty))
                                });
            }
            catch (FormatException)
            {
            }
            return string.Empty;
        });

        if (DangerousBlockRegex().IsMatch(text)) warnings.Add("dangerous-html-removed");
        text = DangerousBlockRegex().Replace(text, string.Empty);
        text = LineBreakRegex().Replace(text, "\n");
        text = ListItemRegex().Replace(text, "\n- $1");
        text = StrongRegex().Replace(text, "**$1**");
        text = EmphasisRegex().Replace(text, "*$1*");
        text = LinkRegex().Replace(text, match => NormalizeLink(match.Groups[1].Value, match.Groups[2].Value, warnings));
        if (RemainingTagRegex().Matches(text).Cast<Match>().Any(match => !ListContainerRegex().IsMatch(match.Value)))
            warnings.Add("unsupported-html-removed");
        text = RemainingTagRegex().Replace(text, string.Empty);
        return NormalizeWhitespace(WebUtility.HtmlDecode(text));
    }

    private static string NormalizeLink(string target, string label, List<string> warnings)
    {
        if (Uri.TryCreate(WebUtility.HtmlDecode(target), UriKind.Absolute, out var uri)
         && uri.Scheme is "http" or "https" or "mailto")
            return $"[{label}]({uri.AbsoluteUri})";
        warnings.Add("unsafe-link-target-removed");
        return label;
    }

    private static string NormalizeWhitespace(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                             .Replace('\r', '\n');
        normalized = HorizontalWhitespaceRegex().Replace(normalized, " ");
        normalized = SpaceAroundNewLineRegex().Replace(normalized, "\n");
        normalized = ExcessBlankLinesRegex().Replace(normalized, "\n\n");
        return normalized.Trim();
    }

    [GeneratedRegex("<\\s*[a-zA-Z][^>]*>", RegexOptions.Singleline)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("<img\\b[^>]*?src\\s*=\\s*['\"]data:(image/(?:png|jpeg|jpg|gif|webp));base64,([^'\"]+)['\"][^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DataImageRegex();

    [GeneratedRegex("<(script|style|iframe|object)\\b[^>]*>.*?</\\1\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DangerousBlockRegex();

    [GeneratedRegex("<\\s*(?:br\\s*/?|/p|p[^>]*|/div|div[^>]*|/h[1-6]|h[1-6][^>]*)\\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex("<li\\b[^>]*>(.*?)</li\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex("<(?:strong|b)\\b[^>]*>(.*?)</(?:strong|b)\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StrongRegex();

    [GeneratedRegex("<(?:em|i)\\b[^>]*>(.*?)</(?:em|i)\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex EmphasisRegex();

    [GeneratedRegex("<a\\b[^>]*href\\s*=\\s*['\"]([^'\"]+)['\"][^>]*>(.*?)</a\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LinkRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex RemainingTagRegex();

    [GeneratedRegex("^<\\s*/?(?:ul|ol)\\s*>$", RegexOptions.IgnoreCase)]
    private static partial Regex ListContainerRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("[\\t\\f\\v ]+")]
    private static partial Regex HorizontalWhitespaceRegex();

    [GeneratedRegex(" *\\n *")]
    private static partial Regex SpaceAroundNewLineRegex();

    [GeneratedRegex("\\n{3,}")]
    private static partial Regex ExcessBlankLinesRegex();
}
