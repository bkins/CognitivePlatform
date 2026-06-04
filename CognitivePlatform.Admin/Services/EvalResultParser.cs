namespace CognitivePlatform.Admin.Services;

/// <summary>
/// A single model's result row from one evaluator run.
/// </summary>
public sealed record EvalModelRow
{
    public DateTime RunDate       { get; init; }
    public string   Model         { get; init; } = string.Empty;
    public double   AvgScore      { get; init; }
    public int      Tests         { get; init; }
    public int      JsonFails     { get; init; }
    public int      IntentFails   { get; init; }
    public int      ParamFails    { get; init; }
    public int      Timeouts      { get; init; }
    public double   TokPerSecond  { get; init; }
}

/// <summary>
/// Parses <c>eval-*.txt</c> result files written by Run-InterpreterEval.ps1.
/// </summary>
public static class EvalResultParser
{
    private const string EvalResultsDir = @"C:\CP\Data\EvalResults";

    /// <summary>
    /// Reads all <c>eval-*.txt</c> files from the EvalResults directory and
    /// returns every model row found, sorted newest-run first.
    /// </summary>
    public static IReadOnlyList<EvalModelRow> LoadAll(string? dir = null)
    {
        var baseDir = dir ?? EvalResultsDir;

        if (!Directory.Exists(baseDir))
            return [];

        var rows = new List<EvalModelRow>();

        foreach (var file in Directory.EnumerateFiles(baseDir, "eval-*.txt").OrderBy(path => path))
        {
            try
            {
                rows.AddRange(ParseFile(file));
            }
            catch
            {
                // Skip unparseable files rather than crashing the UI
            }
        }

        // Most-recent run first
        rows.Sort((rowA, rowB) => rowB.RunDate.CompareTo(rowA.RunDate));

        return rows;
    }

    // ── Parser ────────────────────────────────────────────────────────────────────

    private static IEnumerable<EvalModelRow> ParseFile(string path)
    {
        var lines   = File.ReadAllLines(path);
        var runDate = ExtractDate(lines, path);

        var inSummary       = false;
        var headerSeen      = false;
        var separatorCount  = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("MODEL SUMMARY", StringComparison.OrdinalIgnoreCase))
            {
                inSummary = true;
                continue;
            }

            if (!inSummary)
                continue;

            if (line.StartsWith("---"))
            {
                separatorCount++;

                // First separator is after the "MODEL SUMMARY" heading,
                // second is after the header row — data follows after the second.
                if (separatorCount == 2)
                    headerSeen = true;

                continue;
            }

            if (line.StartsWith("==="))
                break;  // end of summary block

            if (!headerSeen || string.IsNullOrWhiteSpace(line))
                continue;

            var row = TryParseModelRow(line, runDate);
            if (row is not null)
                yield return row;
        }
    }

    private static EvalModelRow? TryParseModelRow(string line, DateTime runDate)
    {
        // Data rows are space-separated with 9 tokens:
        // Model  AvgScore  Tests  JsonFails  IntentFails  ParamFails  FailTypeCount  Timeouts  Tok/s
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 9)
            return null;

        if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var avgScore))
            return null;

        int.TryParse(parts[2], out var tests);
        int.TryParse(parts[3], out var jsonFails);
        int.TryParse(parts[4], out var intentFails);
        int.TryParse(parts[5], out var paramFails);
        int.TryParse(parts[7], out var timeouts);
        double.TryParse(parts[8], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tokPerSec);

        return new EvalModelRow
               {
                       RunDate      = runDate
                     , Model        = parts[0]
                     , AvgScore     = avgScore
                     , Tests        = tests
                     , JsonFails    = jsonFails
                     , IntentFails  = intentFails
                     , ParamFails   = paramFails
                     , Timeouts     = timeouts
                     , TokPerSecond = tokPerSec
               };
    }

    private static DateTime ExtractDate(string[] lines, string filePath)
    {
        // Prefer the "Generated :" header in the file content
        foreach (var line in lines)
        {
            if (!line.StartsWith("Generated", StringComparison.OrdinalIgnoreCase))
                continue;

            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0)
                continue;

            var dateStr = line[(colonIndex + 1)..].Trim();
            if (DateTime.TryParse(dateStr, System.Globalization.CultureInfo.InvariantCulture
                                , System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                return parsed.ToLocalTime();
        }

        // Fall back to the date embedded in the file name (eval-yyyy-MM-dd.txt)
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName.Length >= 15
         && DateTime.TryParseExact(fileName[5..], "yyyy-MM-dd"
                                  , System.Globalization.CultureInfo.InvariantCulture
                                  , System.Globalization.DateTimeStyles.None, out var fileDate))
            return fileDate;

        return File.GetLastWriteTime(filePath);
    }
}
